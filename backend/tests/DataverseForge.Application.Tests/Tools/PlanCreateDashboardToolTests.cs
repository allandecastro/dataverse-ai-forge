using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateDashboardToolTests
{
    /// <summary>
    /// Builds a tool with an "empty environment" — GetViewsListAsync /
    /// GetChartsListAsync return empty lists so the new id-verification path
    /// short-circuits and the existing tests behave the same. Tests that
    /// exercise the verification pass their own client mock via
    /// <see cref="BuildToolWithClient(Mock{IDataverseClient})"/>.
    /// </summary>
    private static PlanCreateDashboardTool BuildTool()
    {
        var client = new Mock<IDataverseClient>();
        client.Setup(c => c.GetViewsListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ViewInfo>());
        client.Setup(c => c.GetChartsListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChartInfo>());
        return BuildToolWithClient(client);
    }

    private static PlanCreateDashboardTool BuildToolWithClient(Mock<IDataverseClient> client)
    {
        var stored = new Dictionary<string, ProjectState>();
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState(); stored[id] = s; }
                return s;
            });
        store.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");
        return new PlanCreateDashboardTool(client.Object, store.Object, ctx.Object, new XsdValidator());
    }

    [Fact]
    public async Task PlanCreateDashboard_Happy_Path_BuildsFormXmlAndStoresPlanId()
    {
        var tool = BuildTool();
        var visId = Guid.NewGuid();
        var viewId = Guid.NewGuid();

        var tabsJson = $$"""
        {
          "tabs": [
            {
              "name": "main", "label": "Main",
              "columns": [
                {
                  "width": "100%",
                  "sections": [
                    {
                      "name": "kpis", "columns": 1,
                      "cells": [
                        {
                          "cellType": "chart",
                          "rowSpan": 12,
                          "targetEntityType": "opportunity",
                          "viewId": "{{viewId}}",
                          "visualizationId": "{{visId}}"
                        }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var plan = await tool.PlanCreateDashboardAsync(
            dashboardName: "Sales KPIs",
            description: "Org-owned KPI dashboard",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.StartsWith("create_dashboard_", plan.PlanId);
        Assert.Contains("<form>", plan.FormXml);
        Assert.Contains("<ChartGridMode>Chart</ChartGridMode>", plan.FormXml);
        // The OData payload has type=0 and isdefault=false — dashboards never
        // ship as the org default from the agent.
        Assert.Contains("\"type\": 0", plan.WebApiPayloadJson);
        Assert.Contains("\"isdefault\": false", plan.WebApiPayloadJson);
        // objecttypecode=none — dashboards aren't bound to a single entity.
        Assert.Contains("\"objecttypecode\": \"none\"", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateDashboard_EmptyTabsJson_Rejected()
    {
        var tool = BuildTool();
        var plan = await tool.PlanCreateDashboardAsync(
            dashboardName: "X", description: "", tabsJson: "",
            cancellationToken: CancellationToken.None);
        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("tabsJson is required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateDashboard_InvalidJson_Rejected()
    {
        var tool = BuildTool();
        var plan = await tool.PlanCreateDashboardAsync(
            dashboardName: "X", description: "", tabsJson: "not actually json",
            cancellationToken: CancellationToken.None);
        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("not valid JSON", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateDashboard_ChartCellMissingVisualizationId_Rejected()
    {
        var tool = BuildTool();
        var tabsJson = """
        {"tabs":[{"name":"m","columns":[{"width":"100%","sections":[
          {"name":"s","cells":[{"cellType":"chart","targetEntityType":"opportunity"}]}
        ]}]}]}
        """;
        var plan = await tool.PlanCreateDashboardAsync(
            "X", "", tabsJson, CancellationToken.None);
        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("visualizationId is required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateDashboard_FabricatedViewId_RejectedWithRealList()
    {
        // Real session 2026-05-05: agent invented view GUID 1ec44c71-…3ded
        // and the dashboard create looped 4× with opaque "SavedQuery does not
        // exist". The plan tool now lists the entity's real views once and
        // rejects up-front if the ID isn't in the list.
        var realViewId = Guid.NewGuid();
        var fabricatedViewId = Guid.NewGuid();
        var visId = Guid.NewGuid();

        var client = new Mock<IDataverseClient>();
        client.Setup(c => c.GetViewsListAsync("opportunity", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ViewInfo>
            {
                new(ViewId: realViewId, Name: "My Opportunities", Description: "",
                    FetchXml: "<fetch/>", LayoutXml: "<grid/>")
            });
        client.Setup(c => c.GetChartsListAsync("opportunity", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChartInfo>
            {
                new(SavedQueryVisualizationId: visId, Name: "Pipeline by Stage",
                    Description: "", PrimaryEntityTypeCode: "opportunity",
                    IsDefault: false, IsManaged: false)
            });
        var tool = BuildToolWithClient(client);

        var tabsJson = $$"""
        {"tabs":[{"name":"m","columns":[{"width":"100%","sections":[
          {"name":"s","cells":[{"cellType":"chart","targetEntityType":"opportunity",
            "viewId":"{{fabricatedViewId}}","visualizationId":"{{visId}}"}]}
        ]}]}]}
        """;
        var plan = await tool.PlanCreateDashboardAsync(
            "X", "", tabsJson, CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w =>
            w.Contains("does not exist on 'opportunity'", StringComparison.Ordinal) &&
            w.Contains(realViewId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateDashboard_StoresPayloadUnderPlanId()
    {
        var tool = BuildTool();
        var tabsJson = $$"""
        {"tabs":[{"name":"m","columns":[{"width":"100%","sections":[
          {"name":"s","cells":[{"cellType":"iframe","iframeUrl":"https://example.com"}]}
        ]}]}]}
        """;
        var plan = await tool.PlanCreateDashboardAsync(
            "Iframe Dash", "", tabsJson, CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        // Ensure the payload contains a Url element (planId-by-reference: the
        // body the agent will pass to execute is keyed under planId).
        Assert.Contains("https://example.com", plan.WebApiPayloadJson);
    }
}
