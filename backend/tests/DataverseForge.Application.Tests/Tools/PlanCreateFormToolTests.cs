using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateFormToolTests
{
    /// <summary>
    /// Builds a Plan tool with a mocked IDataverseClient that reports the given
    /// columns as existing on the table. Validation in the Plan tool checks each
    /// referenced field against this set, so tests must opt into the columns
    /// they reference.
    /// </summary>
    private static PlanCreateFormTool ToolWithColumns(params string[] columnLogicalNames)
    {
        var mock = new Mock<IDataverseClient>();
        var columns = columnLogicalNames
            .Select(n => new ColumnInfo(
                LogicalName: n,
                DisplayName: n,
                AttributeType: "StringType",
                IsCustom: true,
                RequiredLevel: "None",
                MaxLength: 200,
                IsSearchable: true))
            .ToArray();
        mock.Setup(c => c.GetTableDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableDetails(
                "x", "X", "X", true, false, "UserOwned", "xid", "name",
                columns, Array.Empty<RelationshipInfo>(), Array.Empty<AlternateKeyInfo>()));

        var stored = new Dictionary<string, ProjectState>();
        var mockStore = new Mock<IProjectStateStore>();
        mockStore.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState(); stored[id] = s; }
                return s;
            });
        mockStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);
        var mockCtx = new Mock<ISessionContext>();
        mockCtx.SetupGet(c => c.SessionId).Returns("test-session");
        return new PlanCreateFormTool(mock.Object, mockStore.Object, mockCtx.Object, new XsdValidator());
    }

    [Fact]
    public async Task PlanCreateForm_MainForm_ReturnsValidPlan()
    {
        var tool = ToolWithColumns("cr123_name", "cr123_priority", "cr123_status", "cr123_description", "cr123_duedate", "statuscode", "ownerid");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "cr123_workorder",
            formName: "Work Order Main Form",
            formType: "main",
            description: "Main form for work orders",
            tab1Fields: "cr123_name,cr123_priority,cr123_status",
            tab2Fields: "cr123_description,cr123_duedate",
            tab3Subgrids: "cr123_workorderline",
            includeTimeline: true,
            headerFields: "statuscode,ownerid",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("cr123_workorder", plan.TableLogicalName);
        Assert.Equal("Work Order Main Form", plan.FormName);
        Assert.Equal("main", plan.FormType);
        Assert.NotEmpty(plan.FormXml);
        Assert.NotEmpty(plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateForm_MainForm_FormXmlContainsTabs()
    {
        var tool = ToolWithColumns("cr123_name", "cr123_notes", "statuscode");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "cr123_project",
            formName: "Project Form",
            formType: "main",
            description: "Main project form",
            tab1Fields: "cr123_name",
            tab2Fields: "cr123_notes",
            tab3Subgrids: "cr123_task",
            includeTimeline: true,
            headerFields: "statuscode",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        // Tab labels match the description on each tab's <labels><label> element
        // (Dataverse-emitted forms don't carry `name="tab_*"` attributes; we
        // identify tabs by their label).
        Assert.Contains("description=\"General\"", plan.FormXml);
        Assert.Contains("description=\"Details\"", plan.FormXml);
        Assert.Contains("description=\"Related\"", plan.FormXml);
        Assert.Contains("description=\"Timeline\"", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_FormXml_UsesRealGuidsNotPlaceholders()
    {
        // Regression: tab/section ids used to be "{TAB_GENERAL}", "{SEC_GENERAL}"
        // literal strings — Dataverse rejected the payload with HTTP 400.
        var tool = ToolWithColumns("cr123_name");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "cr123_workorder",
            formName: "Form",
            formType: "main",
            description: "Test",
            tab1Fields: "cr123_name",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: true,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.DoesNotContain("{TAB_GENERAL}", plan.FormXml);
        Assert.DoesNotContain("{SEC_GENERAL}", plan.FormXml);
        Assert.DoesNotContain("{TAB_TIMELINE}", plan.FormXml);
        Assert.DoesNotContain("{TIMELINE_CONTROL}", plan.FormXml);
        // Each tab id should be a 38-char braced GUID like {01234567-89AB-CDEF-0123-456789ABCDEF}
        var tabIdMatch = System.Text.RegularExpressions.Regex.Matches(plan.FormXml, @"<tab[^>]+id=""\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}""");
        Assert.True(tabIdMatch.Count >= 2, "Expected at least 2 tabs with real GUID ids");
    }

    [Fact]
    public async Task PlanCreateForm_FormXml_EveryCellHasIdAttribute()
    {
        // Dataverse REQUIRES `id` on every <cell> element (FormXmlCellCommon
        // schema group). <row> id is optional — Dataverse-emitted forms commonly
        // omit it (verified against a real systemforms PATCH payload). This
        // test only asserts the strict cell rule.
        var tool = ToolWithColumns("cr123_name", "cr123_notes", "statuscode", "ownerid", "createdon");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "cr123_workorder",
            formName: "Form",
            formType: "main",
            description: "Test",
            tab1Fields: "cr123_name",
            tab2Fields: "cr123_notes",
            tab3Subgrids: "cr123_task",
            includeTimeline: true,
            headerFields: "statuscode,ownerid,createdon",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        // Every <cell ...> must include id="..." (lookahead `(?=[\s/>])`
        // distinguishes the tag from the plural container `<cells>`).
        var cellsWithoutId = System.Text.RegularExpressions.Regex.Matches(
            plan.FormXml, @"<cell(?=[\s/>])(?![^>]*\bid=)[^>]*>");
        Assert.True(cellsWithoutId.Count == 0,
            $"Found {cellsWithoutId.Count} <cell> element(s) without id attribute. FormXml:\n{plan.FormXml}");

        // Every <tab ...> must include id="..."
        var tabsWithoutId = System.Text.RegularExpressions.Regex.Matches(
            plan.FormXml, @"<tab(?=[\s/>])(?![^>]*\bid=)[^>]*>");
        Assert.True(tabsWithoutId.Count == 0,
            $"Found {tabsWithoutId.Count} <tab> element(s) without id attribute. FormXml:\n{plan.FormXml}");

        // Every <section ...> must include id="..."
        var sectionsWithoutId = System.Text.RegularExpressions.Regex.Matches(
            plan.FormXml, @"<section(?=[\s/>])(?![^>]*\bid=)[^>]*>");
        Assert.True(sectionsWithoutId.Count == 0,
            $"Found {sectionsWithoutId.Count} <section> element(s) without id attribute. FormXml:\n{plan.FormXml}");

        // <header> must include id="..." — Dataverse runtime validator rejects
        // the form at "line 0, position 0" with "required attribute 'id' is
        // missing" if header has no id. The published schema doesn't show this
        // requirement, but the runtime enforces it.
        var headersWithoutId = System.Text.RegularExpressions.Regex.Matches(
            plan.FormXml, @"<header(?=[\s/>])(?![^>]*\bid=)[^>]*>");
        Assert.True(headersWithoutId.Count == 0,
            $"Found {headersWithoutId.Count} <header> element(s) without id attribute. FormXml:\n{plan.FormXml}");

        // No labelid="..." with a non-GUID value (must be FormGuidType per spec).
        // We removed labelid entirely; assert it's not present.
        Assert.DoesNotContain(" labelid=\"General\"", plan.FormXml);
        Assert.DoesNotContain(" labelid=\"Details\"", plan.FormXml);
        Assert.DoesNotContain(" labelid=\"Related\"", plan.FormXml);
        Assert.DoesNotContain(" labelid=\"Timeline\"", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_MissingFieldOnTable_ReturnsRejectedPlan()
    {
        // Regression: agent passed eppc_ContactId for a lookup that didn't exist;
        // Dataverse returned HTTP 400 with no useful body. Plan tool now refuses
        // to plan a form whose fields aren't on the table.
        var tool = ToolWithColumns("cr123_name"); // ONLY cr123_name exists
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "cr123_workorder",
            formName: "Form",
            formType: "main",
            description: "Test",
            tab1Fields: "cr123_name,cr123_DOES_NOT_EXIST",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("REJECTED"));
        Assert.Contains(plan.Warnings, w => w.Contains("cr123_does_not_exist", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(plan.FormXml);
        Assert.Empty(plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateForm_PascalCaseFieldNames_NormalizedToLowercase()
    {
        // Regression: agent passed eppc_GiftName/eppc_Description (PascalCase) but
        // Dataverse logical names are lowercase. The form XML must use the
        // lowercase form in datafieldname attributes or Dataverse rejects it.
        var tool = ToolWithColumns("eppc_giftname", "eppc_description");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "Gift Form",
            formType: "main",
            description: "",
            tab1Fields: "eppc_GiftName,eppc_Description",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        // datafieldname must be the lowercase logical name
        Assert.Contains("datafieldname=\"eppc_giftname\"", plan.FormXml);
        Assert.Contains("datafieldname=\"eppc_description\"", plan.FormXml);
        Assert.DoesNotContain("datafieldname=\"eppc_GiftName\"", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_QuickCreate_WarnsOnTooManyFields()
    {
        var tool = ToolWithColumns("cr123_name", "cr123_priority", "cr123_status", "cr123_owner", "cr123_due", "cr123_category", "cr123_description", "cr123_notes");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "cr123_task",
            formName: "Quick Create Task",
            formType: "quickCreate",
            description: "Quick create for tasks",
            tab1Fields: "cr123_name,cr123_priority,cr123_status,cr123_owner,cr123_due,cr123_category,cr123_description,cr123_notes",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("5-7 fields"));
    }

    [Fact]
    public async Task PlanCreateForm_QuickCreate_WarnsOnSubgrids()
    {
        var tool = ToolWithColumns("cr123_name");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "cr123_task",
            formName: "Quick Create Task",
            formType: "quickCreate",
            description: "Quick create",
            tab1Fields: "cr123_name",
            tab2Fields: "",
            tab3Subgrids: "cr123_subtask",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("subgrids"));
    }

    [Fact]
    public async Task PlanCreateForm_HeaderExceedsFourFields_Warning()
    {
        var tool = ToolWithColumns("cr123_name", "cr123_priority", "cr123_extra", "statuscode", "ownerid", "createdon");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "cr123_workorder",
            formName: "WO Form",
            formType: "main",
            description: "Test",
            tab1Fields: "cr123_name",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "statuscode,ownerid,createdon,cr123_priority,cr123_extra",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("max 4 fields"));
    }

    [Fact]
    public async Task PlanCreateForm_MainForm_RiskLevelIsOrange()
    {
        var tool = ToolWithColumns("cr123_name");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "cr123_workorder",
            formName: "WO Form",
            formType: "main",
            description: "Test",
            tab1Fields: "cr123_name",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanCreateForm_QuickCreate_RiskLevelIsGreen()
    {
        var tool = ToolWithColumns("cr123_name");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "cr123_task",
            formName: "Quick Create",
            formType: "quickCreate",
            description: "Test",
            tab1Fields: "cr123_name",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.Equal("green", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanCreateForm_PayloadContainsFormXml()
    {
        var tool = ToolWithColumns("cr123_name");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "cr123_workorder",
            formName: "WO Form",
            formType: "main",
            description: "Test",
            tab1Fields: "cr123_name",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.Contains("formxml", plan.WebApiPayloadJson);
        Assert.Contains("objecttypecode", plan.WebApiPayloadJson);
        Assert.Contains("cr123_workorder", plan.WebApiPayloadJson);
    }

    // ───────────────────────────────────────────────────────────────────
    // Structured layout (tabsJson) — phase-1 of "Improve form management"
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanCreateForm_TabsJson_TwoColumnLayout_EmitsBothColumns()
    {
        // Real-world example: an Identity column on the left + a Pricing column
        // on the right, each with their own section. Tests that 50/50 widths
        // pass validation and both columns appear in the FormXML.
        var tool = ToolWithColumns("eppc_giftname", "eppc_giftcode", "eppc_value", "eppc_quantity");
        var tabsJson = """
        {
          "tabs": [{
            "name": "General",
            "columns": [
              { "width": "50%", "sections": [{ "name": "Identity", "cells": [
                  {"field":"eppc_giftname"}, {"field":"eppc_giftcode"} ] }] },
              { "width": "50%", "sections": [{ "name": "Pricing", "cells": [
                  {"field":"eppc_value"}, {"field":"eppc_quantity"} ] }] }
            ]
          }]
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "Gift Form",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("<column width=\"50%\">", plan.FormXml);
        // Both column widths should appear — single column tab would produce just one width.
        var fiftyPercentMatches = System.Text.RegularExpressions.Regex.Matches(
            plan.FormXml, "<column width=\"50%\">");
        Assert.Equal(2, fiftyPercentMatches.Count);
        // Both sections should be rendered.
        Assert.Contains("description=\"Identity\"", plan.FormXml);
        Assert.Contains("description=\"Pricing\"", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_OverridesLegacyCsvFields()
    {
        // When tabsJson is provided, the legacy tab1Fields CSV must be ignored
        // entirely — we don't want a hybrid form where some fields come from
        // each input.
        var tool = ToolWithColumns("eppc_giftname", "eppc_legacy_only", "eppc_structured_only");
        var tabsJson = """
        {"tabs":[{"name":"T1","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{"field":"eppc_structured_only"}]}]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "eppc_legacy_only",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Contains("datafieldname=\"eppc_structured_only\"", plan.FormXml);
        Assert.DoesNotContain("datafieldname=\"eppc_legacy_only\"", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_ColumnWidthsDontSumTo100_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T1","columns":[
          {"width":"40%","sections":[{"name":"A","cells":[{"field":"eppc_giftname"}]}]},
          {"width":"40%","sections":[]}
        ]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("80%") || w.Contains("100%"));
        Assert.Empty(plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_FourColumns_Rejected()
    {
        // FormXml.xsd hard limit: max 3 columns per tab.
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T1","columns":[
          {"width":"25%","sections":[]},
          {"width":"25%","sections":[]},
          {"width":"25%","sections":[]},
          {"width":"25%","sections":[]}
        ]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("limit of 3") || w.Contains("exceeds"));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_StructuredFieldDoesntExist_Rejected()
    {
        // Structured cells must be validated against the live schema, same as
        // the legacy CSVs. Past failure pattern: REJECTED report only listed
        // CSV fields — structured cells slipped through and Dataverse 400'd.
        var tool = ToolWithColumns("eppc_giftname"); // no eppc_does_not_exist
        var tabsJson = """
        {"tabs":[{"name":"T1","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[
            {"field":"eppc_giftname"},
            {"field":"eppc_does_not_exist"}
          ]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("eppc_does_not_exist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_Subgrid_EmitsTargetEntityAndOptionalRelationship()
    {
        // Subgrid cells live alongside data fields in a section — that's the
        // big UX win over the legacy "all subgrids on tab 3" layout.
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"General","columns":[{"width":"100%","sections":[
          {"name":"Header","cells":[{"field":"eppc_giftname"}]},
          {"name":"Lines","cells":[
            {"controlType":"subgrid","targetEntity":"eppc_giftline","relationshipName":"eppc_gift_giftline"}
          ]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("<TargetEntityType>eppc_giftline</TargetEntityType>", plan.FormXml);
        Assert.Contains("<RelationshipName>eppc_gift_giftline</RelationshipName>", plan.FormXml);
        // Standard subgrid classid.
        Assert.Contains("classid=\"{E7A81278-8635-4D9E-8D4D-59480B391C5B}\"", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_InvalidJson_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "{ this is not valid json",
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("not valid JSON"));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_BareTabsArray_Accepted()
    {
        // Convenience shape: agent passes just an array of tabs (no wrapping
        // object). The parser accepts it and wraps internally.
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        [{"name":"T1","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{"field":"eppc_giftname"}]}
        ]}]}]
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("datafieldname=\"eppc_giftname\"", plan.FormXml);
    }

    // ───────────────────────────────────────────────────────────────────
    // Phase 2 — richer subgrid params
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanCreateForm_TabsJson_Subgrid_AllParams_Emitted()
    {
        // Real-world subgrid in a Gift form: shows lines (Items), uses a custom
        // view, paginates 25 per page, can render a chart alongside the grid.
        var tool = ToolWithColumns("eppc_giftname");
        var visualizationId = Guid.NewGuid().ToString();
        var tabsJson = $$"""
        {"tabs":[{"name":"General","columns":[{"width":"100%","sections":[
          {"name":"Items","cells":[{
            "controlType":"subgrid",
            "targetEntity":"eppc_giftline",
            "relationshipName":"eppc_gift_giftline",
            "viewIds":"{11111111-1111-1111-1111-111111111111},{22222222-2222-2222-2222-222222222222}",
            "autoExpand":"Fixed",
            "recordsPerPage":25,
            "maxRowsBeforeScroll":10,
            "enableJumpBar":true,
            "chartGridMode":"ChartAndGrid",
            "visualizationId":"{{visualizationId}}",
            "enableChartPicker":true,
            "enableContextualActions":false
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("<TargetEntityType>eppc_giftline</TargetEntityType>", plan.FormXml);
        Assert.Contains("<RelationshipName>eppc_gift_giftline</RelationshipName>", plan.FormXml);
        Assert.Contains("<ViewIds>{11111111-1111-1111-1111-111111111111},{22222222-2222-2222-2222-222222222222}</ViewIds>", plan.FormXml);
        Assert.Contains("<AutoExpand>Fixed</AutoExpand>", plan.FormXml);
        Assert.Contains("<RecordsPerPage>25</RecordsPerPage>", plan.FormXml);
        Assert.Contains("<MaxRowsBeforeScroll>10</MaxRowsBeforeScroll>", plan.FormXml);
        Assert.Contains("<EnableJumpBar>true</EnableJumpBar>", plan.FormXml);
        Assert.Contains("<ChartGridMode>ChartAndGrid</ChartGridMode>", plan.FormXml);
        Assert.Contains($"<VisualizationId>{visualizationId}</VisualizationId>", plan.FormXml);
        Assert.Contains("<EnableChartPicker>true</EnableChartPicker>", plan.FormXml);
        Assert.Contains("<EnableContextualActions>false</EnableContextualActions>", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_Subgrid_OptionalParamsNotEmittedWhenUnset()
    {
        // The optional params (AutoExpand, RecordsPerPage, etc.) are nullable
        // by design — when the agent doesn't set them, we MUST NOT emit them
        // so we don't override Dataverse defaults silently.
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{"controlType":"subgrid","targetEntity":"eppc_giftline"}]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.DoesNotContain("<AutoExpand>", plan.FormXml);
        Assert.DoesNotContain("<RecordsPerPage>", plan.FormXml);
        Assert.DoesNotContain("<MaxRowsBeforeScroll>", plan.FormXml);
        Assert.DoesNotContain("<EnableJumpBar>", plan.FormXml);
        Assert.DoesNotContain("<ChartGridMode>", plan.FormXml);
        Assert.DoesNotContain("<VisualizationId>", plan.FormXml);
        Assert.DoesNotContain("<EnableChartPicker>", plan.FormXml);
        // EnableQuickFind / EnableViewPicker are NOT nullable — they're
        // boolean defaults so they always appear. That's fine.
        Assert.Contains("<EnableQuickFind>true</EnableQuickFind>", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_Subgrid_InvalidAutoExpand_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "controlType":"subgrid",
            "targetEntity":"eppc_giftline",
            "autoExpand":"Banana"
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("autoExpand", StringComparison.OrdinalIgnoreCase) && w.Contains("Banana"));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_Subgrid_InvalidRecordsPerPage_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "controlType":"subgrid",
            "targetEntity":"eppc_giftline",
            "recordsPerPage":500
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("recordsPerPage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_Subgrid_VisualizationWithoutChartGridMode_Rejected()
    {
        // Coherence rule: setting visualizationId or enableChartPicker without
        // chartGridMode means the chart silently won't render. We surface this
        // as a hard reject so the agent fixes the spec instead of shipping a
        // broken-looking form.
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = $$"""
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "controlType":"subgrid",
            "targetEntity":"eppc_giftline",
            "visualizationId":"{{Guid.NewGuid()}}"
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("chartGridMode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_Subgrid_VisualizationIdAllZeros_Rejected()
    {
        // Real-session failure: agent set visualizationId to the all-zero GUID
        // (presumably as a placeholder), Guid.TryParse accepted it, plan
        // succeeded, then Dataverse returned HTTP 400 "Expected non-empty Guid"
        // on CreateForm. Validation now rejects Guid.Empty up front.
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "controlType":"subgrid",
            "targetEntity":"eppc_giftline",
            "chartGridMode":"ChartAndGrid",
            "visualizationId":"00000000-0000-0000-0000-000000000000"
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("all-zero GUID", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Warnings, w => w.Contains("Expected non-empty Guid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_Subgrid_MissingTargetEntity_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{"controlType":"subgrid"}]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("targetEntity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_RowspanColspan_Emitted()
    {
        // Per-cell rowspan/colspan are how multi-line text and notes-like
        // controls get vertical room.
        var tool = ToolWithColumns("eppc_description");
        var tabsJson = """
        {"tabs":[{"name":"T1","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{"field":"eppc_description","rowspan":4,"colspan":2}]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F",
            formType: "main",
            description: "",
            tab1Fields: "",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("rowspan=\"4\"", plan.FormXml);
        Assert.Contains("colspan=\"2\"", plan.FormXml);
    }

    // ───────────────────────────────────────────────────────────────────
    // Phase 3 — control-type catalog (lookup, textarea, webresource, quickform)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanCreateForm_TabsJson_TextAreaFormat_Emitted()
    {
        // A "Description" field rendered as multi-line: Format=TextArea,
        // MaxLength=4000. Smoke test for the typed Params block.
        var tool = ToolWithColumns("eppc_description");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "field":"eppc_description",
            "params":{"format":"TextArea","maxLength":4000}
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("<Format>TextArea</Format>", plan.FormXml);
        Assert.Contains("<MaxLength>4000</MaxLength>", plan.FormXml);
        // The control element must NOT self-close when it carries parameters —
        // self-closing means we lost the params block during emission.
        Assert.DoesNotContain("datafieldname=\"eppc_description\" />", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_NumericRange_Emitted()
    {
        // Numeric field with min/max/precision. Tests the double + int helpers.
        var tool = ToolWithColumns("eppc_value");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "field":"eppc_value",
            "params":{"minValue":0,"maxValue":100000.5,"precision":2}
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("<MinValue>0</MinValue>", plan.FormXml);
        // Invariant culture — must use a dot, not a comma.
        Assert.Contains("<MaxValue>100000.5</MaxValue>", plan.FormXml);
        Assert.Contains("<Precision>2</Precision>", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_LookupParams_Emitted()
    {
        // Lookup-specific params on a default/lookup cell.
        var tool = ToolWithColumns("eppc_contactid");
        var defaultViewId = Guid.NewGuid().ToString();
        var tabsJson = $$"""
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "field":"eppc_contactid",
            "controlType":"lookup",
            "params":{
              "defaultViewId":"{{defaultViewId}}",
              "filterRelationshipName":"eppc_contact_eppc_gift_eppc_contactid",
              "disableMru":true,
              "disableQuickFind":false,
              "isInlineNewEnabled":true
            }
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains($"<DefaultViewId>{defaultViewId}</DefaultViewId>", plan.FormXml);
        Assert.Contains("<FilterRelationshipName>eppc_contact_eppc_gift_eppc_contactid</FilterRelationshipName>", plan.FormXml);
        Assert.Contains("<DisableMru>true</DisableMru>", plan.FormXml);
        Assert.Contains("<DisableQuickFind>false</DisableQuickFind>", plan.FormXml);
        Assert.Contains("<IsInlineNewEnabled>true</IsInlineNewEnabled>", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_WebResource_Emitted()
    {
        // Web resource cell — different classid + isunbound + WebResourceId is required.
        var tool = ToolWithColumns("eppc_giftname");
        var webResourceId = Guid.NewGuid().ToString();
        var tabsJson = $$"""
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "controlType":"webresource",
            "params":{
              "webResourceId":"{{webResourceId}}",
              "url":"/WebResources/new_chartHelper.html",
              "height":300,
              "width":600,
              "altText":"Chart helper",
              "sizeType":"Specific",
              "horizontalAlignment":"Center",
              "showOnMobileClient":true,
              "passParameters":true
            }
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        // WebResource classid (not the generic data classid).
        Assert.Contains("classid=\"{9FDF5F91-88B1-47F4-AD53-C11EFC01A01D}\"", plan.FormXml);
        Assert.Contains("isunbound=\"true\"", plan.FormXml);
        Assert.Contains($"<WebResourceId>{webResourceId}</WebResourceId>", plan.FormXml);
        Assert.Contains("<Url>/WebResources/new_chartHelper.html</Url>", plan.FormXml);
        Assert.Contains("<Height>300</Height>", plan.FormXml);
        Assert.Contains("<Width>600</Width>", plan.FormXml);
        Assert.Contains("<SizeType>Specific</SizeType>", plan.FormXml);
        Assert.Contains("<HorizontalAlignment>Center</HorizontalAlignment>", plan.FormXml);
        Assert.Contains("<ShowOnMobileClient>true</ShowOnMobileClient>", plan.FormXml);
        Assert.Contains("<PassParameters>true</PassParameters>", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_WebResource_MissingResourceId_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "controlType":"webresource",
            "params":{"url":"/x.html"}
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("webResourceId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_WebResource_InvalidSizeType_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = $$"""
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "controlType":"webresource",
            "params":{"webResourceId":"{{Guid.NewGuid()}}","sizeType":"Banana"}
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("sizeType", StringComparison.OrdinalIgnoreCase) && w.Contains("Banana"));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_QuickForm_Emitted()
    {
        // Quick form — bound to a lookup field, with one or more quick view form GUIDs.
        var tool = ToolWithColumns("eppc_contactid");
        var qfGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var tabsJson = $$"""
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "field":"eppc_contactid",
            "controlType":"quickform",
            "params":{"quickForms":"{{qfGuid}}","controlMode":"View"}
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("classid=\"{069810AC-4998-4D33-AAF1-1841F7544F95}\"", plan.FormXml);
        Assert.Contains("datafieldname=\"eppc_contactid\"", plan.FormXml);
        Assert.Contains($"<QuickForms>{qfGuid}</QuickForms>", plan.FormXml);
        Assert.Contains("<ControlMode>View</ControlMode>", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_QuickForm_MissingForms_Rejected()
    {
        var tool = ToolWithColumns("eppc_contactid");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{
            "field":"eppc_contactid",
            "controlType":"quickform"
          }]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("quickForms", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_UnknownControlType_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{"field":"eppc_giftname","controlType":"unicorn"}]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("controlType", StringComparison.OrdinalIgnoreCase) && w.Contains("unicorn"));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_InvalidFormat_Rejected()
    {
        var tool = ToolWithColumns("eppc_description");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{"field":"eppc_description","params":{"format":"Hieroglyphs"}}]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("format", StringComparison.OrdinalIgnoreCase) && w.Contains("Hieroglyphs"));
    }

    // ───────────────────────────────────────────────────────────────────
    // Phase 4 — Card form type (SystemForm.Type = 11)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanCreateForm_Card_MapsToType11()
    {
        var tool = ToolWithColumns("eppc_giftname", "eppc_value");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "Gift Card",
            formType: "card",
            description: "Tile layout",
            tab1Fields: "eppc_giftname,eppc_value",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        // The OData payload must include `"type": 11` — card type code.
        Assert.Contains("\"type\": 11", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateForm_Card_TooManyFields_Warning()
    {
        var tool = ToolWithColumns("a", "b", "c", "d", "e", "f", "g");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "Card",
            formType: "card",
            description: "",
            tab1Fields: "a,b,c,d,e,f,g",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("max 4-5 fields"));
    }

    [Fact]
    public async Task PlanCreateForm_Card_WithSubgrid_Warning()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "Card",
            formType: "card",
            description: "",
            tab1Fields: "eppc_giftname",
            tab2Fields: "",
            tab3Subgrids: "eppc_giftline",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("Card forms should not contain subgrids"));
    }

    [Fact]
    public async Task PlanCreateForm_Card_DoesNotEmitFooter()
    {
        // Card forms aren't supposed to have a footer — verifies the gate
        // around EmitEmptyFooter is checking for "main" only, not just
        // "anything that isn't quickCreate".
        var tool = ToolWithColumns("eppc_giftname");
        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "Card",
            formType: "card",
            description: "",
            tab1Fields: "eppc_giftname",
            tab2Fields: "",
            tab3Subgrids: "",
            includeTimeline: false,
            headerFields: "",
            tabsJson: "",
            cancellationToken: CancellationToken.None);

        Assert.DoesNotContain("<footer", plan.FormXml);
    }

    // ───────────────────────────────────────────────────────────────────
    // Phase 5 — form libraries + event handler bindings
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanCreateForm_TabsJson_LibrariesAndOnLoadEvent_Emitted()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var libGuid = Guid.NewGuid().ToString();
        var tabsJson = $$"""
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"}]}
          ]}]}],
          "libraries":[{"name":"new_/scripts/giftform.js","uniqueId":"{{libGuid}}"}],
          "events":[{
            "eventType":"DataEvent",
            "name":"onload",
            "functionName":"Forge.Gift.onLoad",
            "libraryName":"new_/scripts/giftform.js"
          }]
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("<formLibraries>", plan.FormXml);
        Assert.Contains($"<Library name=\"new_/scripts/giftform.js\" libraryUniqueId=\"{libGuid}\"", plan.FormXml);
        Assert.Contains("<events>", plan.FormXml);
        Assert.Contains("name=\"onload\"", plan.FormXml);
        Assert.Contains("eventType=\"DataEvent\"", plan.FormXml);
        Assert.Contains("functionName=\"Forge.Gift.onLoad\"", plan.FormXml);
        Assert.Contains("libraryName=\"new_/scripts/giftform.js\"", plan.FormXml);
        // PassExecutionContext should default to true.
        Assert.Contains("passExecutionContext=\"true\"", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_ControlEventOnAttribute_Emitted()
    {
        var tool = ToolWithColumns("eppc_giftname", "eppc_value");
        var libGuid = Guid.NewGuid().ToString();
        var tabsJson = $$"""
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"},{"field":"eppc_value"}]}
          ]}]}],
          "libraries":[{"name":"new_/scripts/gift.js","uniqueId":"{{libGuid}}"}],
          "events":[{
            "eventType":"ControlEvent",
            "name":"onchange",
            "functionName":"Forge.Gift.onValueChange",
            "libraryName":"new_/scripts/gift.js",
            "attribute":"eppc_value",
            "passExecutionContext":true
          }]
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("eventType=\"ControlEvent\"", plan.FormXml);
        Assert.Contains("name=\"onchange\"", plan.FormXml);
        Assert.Contains("attribute=\"eppc_value\"", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_ControlEvent_MissingAttributeAndControl_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var libGuid = Guid.NewGuid().ToString();
        var tabsJson = $$"""
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"}]}
          ]}]}],
          "libraries":[{"name":"x.js","uniqueId":"{{libGuid}}"}],
          "events":[{
            "eventType":"ControlEvent",
            "name":"onchange",
            "functionName":"Foo",
            "libraryName":"x.js"
          }]
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w =>
            w.Contains("ControlEvent", StringComparison.OrdinalIgnoreCase)
            && (w.Contains("attribute", StringComparison.OrdinalIgnoreCase) || w.Contains("control", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_EventReferencingUnknownLibrary_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = $$"""
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"}]}
          ]}]}],
          "libraries":[{"name":"a.js","uniqueId":"{{Guid.NewGuid()}}"}],
          "events":[{
            "eventType":"DataEvent",
            "name":"onload",
            "functionName":"Foo",
            "libraryName":"b.js"
          }]
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("libraryName") && w.Contains("b.js"));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_LibraryWithoutValidGuid_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"}]}
          ]}]}],
          "libraries":[{"name":"x.js","uniqueId":"not-a-guid"}]
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("uniqueId") && w.Contains("not-a-guid"));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_NoEventsOrLibraries_NoBlocksEmitted()
    {
        // Without libs/events the form XML must NOT contain empty
        // <formLibraries /> or <events /> blocks — that bloats the XML
        // and Dataverse interprets empty <events> differently from absent.
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{"field":"eppc_giftname"}]}]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.DoesNotContain("<formLibraries", plan.FormXml);
        Assert.DoesNotContain("<events>", plan.FormXml);
    }

    // ───────────────────────────────────────────────────────────────────
    // Phase 6 — display conditions + ancestor inheritance
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanCreateForm_TabsJson_DisplayConditionsEveryone_Emitted()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"}]}
          ]}]}],
          "displayConditions":{"everyone":true}
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("<DisplayConditions>", plan.FormXml);
        Assert.Contains("<Everyone />", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_DisplayConditionsRoles_Emitted()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var role1 = Guid.NewGuid().ToString();
        var role2 = Guid.NewGuid().ToString();
        var tabsJson = $$"""
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"}]}
          ]}]}],
          "displayConditions":{
            "everyone":false,
            "roleIds":["{{role1}}","{{role2}}"],
            "fallbackForm":true,
            "order":2
          }
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains($"<Role Id=\"{role1}\" />", plan.FormXml);
        Assert.Contains($"<Role Id=\"{role2}\" />", plan.FormXml);
        Assert.Contains("FallbackForm=\"true\"", plan.FormXml);
        Assert.Contains("Order=\"2\"", plan.FormXml);
        Assert.DoesNotContain("<Everyone", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_DisplayConditions_BothEveryoneAndRoles_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = $$"""
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"}]}
          ]}]}],
          "displayConditions":{"everyone":true,"roleIds":["{{Guid.NewGuid()}}"]}
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("Everyone") && w.Contains("RoleIds"));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_DisplayConditions_NeitherEveryoneNorRoles_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"}]}
          ]}]}],
          "displayConditions":{"everyone":false}
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("Everyone") || w.Contains("RoleId"));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_DisplayConditions_BadRoleGuid_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"}]}
          ]}]}],
          "displayConditions":{"everyone":false,"roleIds":["not-a-guid"]}
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("not-a-guid"));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_AncestorFormId_Emitted()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var ancestor = Guid.NewGuid().ToString();
        var tabsJson = $$"""
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"}]}
          ]}]}],
          "ancestorFormId":"{{ancestor}}"
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        // <ancestor> must appear before <tabs> per FormXml.xsd's xs:all order.
        var ancestorIdx = plan.FormXml.IndexOf("<ancestor", StringComparison.Ordinal);
        var tabsIdx = plan.FormXml.IndexOf("<tabs>", StringComparison.Ordinal);
        Assert.True(ancestorIdx > 0 && ancestorIdx < tabsIdx,
            $"<ancestor> should appear before <tabs>; ancestorIdx={ancestorIdx}, tabsIdx={tabsIdx}");
        Assert.Contains($"<ancestor id=\"{ancestor}\" />", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_AncestorFormId_BadGuid_Rejected()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {
          "tabs":[{"name":"T","columns":[{"width":"100%","sections":[
            {"name":"S","cells":[{"field":"eppc_giftname"}]}
          ]}]}],
          "ancestorFormId":"banana"
        }
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("ancestorFormId") && w.Contains("banana"));
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_NoPhase6Fields_NoBlocksEmitted()
    {
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{"field":"eppc_giftname"}]}]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.DoesNotContain("<DisplayConditions", plan.FormXml);
        Assert.DoesNotContain("<ancestor", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateForm_TabsJson_NoParams_StillEmitsSelfClosingControl()
    {
        // Regression: when no Params are set, the cell should emit a
        // self-closing <control ... /> element. Don't bloat the FormXML
        // with empty <parameters /> blocks.
        var tool = ToolWithColumns("eppc_giftname");
        var tabsJson = """
        {"tabs":[{"name":"T","columns":[{"width":"100%","sections":[
          {"name":"S","cells":[{"field":"eppc_giftname"}]}
        ]}]}]}
        """;

        var plan = await tool.PlanCreateFormAsync(
            tableLogicalName: "eppc_gift",
            formName: "F", formType: "main", description: "",
            tab1Fields: "", tab2Fields: "", tab3Subgrids: "",
            includeTimeline: false, headerFields: "",
            tabsJson: tabsJson,
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("datafieldname=\"eppc_giftname\" />", plan.FormXml);
        Assert.DoesNotContain("<parameters />", plan.FormXml);
        Assert.DoesNotContain("<parameters></parameters>", plan.FormXml);
    }
}
