using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateSiteMapToolTests
{
    private static (PlanCreateSiteMapTool Tool, Dictionary<string, ProjectState> Stored) BuildTool()
    {
        var stored = new Dictionary<string, ProjectState>();
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState { ProjectId = id }; stored[id] = s; }
                return s;
            });
        store.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");
        return (new PlanCreateSiteMapTool(store.Object, ctx.Object, new XsdValidator()), stored);
    }

    [Fact]
    public async Task PlanCreateSiteMap_Happy_StoresPayloadAndReturnsYellow()
    {
        var (tool, stored) = BuildTool();
        var areasJson = """
        {
          "areas": [
            {
              "id": "demo", "title": "Demo", "showGroups": true,
              "groups": [
                {
                  "id": "customer", "title": "Customer",
                  "subareas": [
                    { "id": "acc", "title": "Accounts", "entity": "account" },
                    { "id": "con", "title": "Contacts", "entity": "contact" }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var plan = await tool.PlanCreateSiteMapAsync(
            siteMapName: "Demo Sitemap",
            siteMapNameUnique: "eppc_DemoSitemap",
            areasJson: areasJson,
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("create_sitemap_", plan.PlanId);
        Assert.Equal("yellow", plan.RiskLevel);
        Assert.Equal("eppc_demositemap", plan.SiteMapNameUnique); // lowercased
        Assert.Contains("<Area Id=\"demo\"", plan.SiteMapXml, StringComparison.Ordinal);
        Assert.Contains("Entity=\"account\"", plan.SiteMapXml, StringComparison.Ordinal);
        Assert.Contains("\"sitemapxml\":", plan.WebApiPayloadJson, StringComparison.Ordinal);
        Assert.True(stored["test-session"].PendingPlans.ContainsKey(plan.PlanId));
    }

    [Fact]
    public async Task PlanCreateSiteMap_RejectsEmptyAreasJson()
    {
        var (tool, _) = BuildTool();

        var plan = await tool.PlanCreateSiteMapAsync(
            siteMapName: "X",
            siteMapNameUnique: "eppc_X",
            areasJson: "",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_create_sitemap_", plan.PlanId);
        Assert.Equal("red", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanCreateSiteMap_RejectsMalformedJson()
    {
        var (tool, _) = BuildTool();

        var plan = await tool.PlanCreateSiteMapAsync(
            siteMapName: "X",
            siteMapNameUnique: "eppc_X",
            areasJson: "{ this is not json",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_create_sitemap_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("not valid JSON"));
    }

    [Fact]
    public async Task PlanCreateSiteMap_RejectsSpecWithDuplicateAreaIds()
    {
        var (tool, _) = BuildTool();
        var areasJson = """
        {
          "areas": [
            { "id": "x", "title": "X1", "showGroups": false, "groups": [] },
            { "id": "x", "title": "X2", "showGroups": false, "groups": [] }
          ]
        }
        """;

        var plan = await tool.PlanCreateSiteMapAsync(
            siteMapName: "X", siteMapNameUnique: "eppc_X",
            areasJson: areasJson, cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_create_sitemap_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("'x' is duplicated"));
    }

    [Fact]
    public async Task PlanCreateSiteMap_RejectsSubAreaWithBothEntityAndUrl()
    {
        var (tool, _) = BuildTool();
        var areasJson = """
        {
          "areas": [{
            "id": "a", "showGroups": true,
            "groups": [{
              "id": "g",
              "subareas": [{ "id": "s", "entity": "account", "url": "https://x" }]
            }]
          }]
        }
        """;

        var plan = await tool.PlanCreateSiteMapAsync(
            siteMapName: "X", siteMapNameUnique: "eppc_X",
            areasJson: areasJson, cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_create_sitemap_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("more than one of entity / url / pageId"));
    }

    [Fact]
    public async Task PlanCreateSiteMap_LowercasesUniqueName()
    {
        var (tool, _) = BuildTool();
        var areasJson = """
        {
          "areas": [{
            "id": "a", "showGroups": false, "groups": []
          }]
        }
        """;

        var plan = await tool.PlanCreateSiteMapAsync(
            siteMapName: "X",
            siteMapNameUnique: "EPPC_DemoMixedCase",
            areasJson: areasJson,
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("create_sitemap_", plan.PlanId);
        Assert.Equal("eppc_demomixedcase", plan.SiteMapNameUnique);
        Assert.Contains(plan.Warnings, w => w.Contains("lowercased"));
    }
}
