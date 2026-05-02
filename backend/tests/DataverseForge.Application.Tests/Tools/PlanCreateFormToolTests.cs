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
        return new PlanCreateFormTool(mock.Object, mockStore.Object, mockCtx.Object);
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
            CancellationToken.None);

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
            CancellationToken.None);

        Assert.Contains("tab_general", plan.FormXml);
        Assert.Contains("tab_details", plan.FormXml);
        Assert.Contains("tab_related", plan.FormXml);
        Assert.Contains("tab_timeline", plan.FormXml);
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
            CancellationToken.None);

        Assert.DoesNotContain("{TAB_GENERAL}", plan.FormXml);
        Assert.DoesNotContain("{SEC_GENERAL}", plan.FormXml);
        Assert.DoesNotContain("{TAB_TIMELINE}", plan.FormXml);
        Assert.DoesNotContain("{TIMELINE_CONTROL}", plan.FormXml);
        // Each tab id should be a 38-char braced GUID like {01234567-89AB-CDEF-0123-456789ABCDEF}
        var tabIdMatch = System.Text.RegularExpressions.Regex.Matches(plan.FormXml, @"<tab[^>]+id=""\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}""");
        Assert.True(tabIdMatch.Count >= 2, "Expected at least 2 tabs with real GUID ids");
    }

    [Fact]
    public async Task PlanCreateForm_FormXml_EveryRowAndCellHasIdAttribute()
    {
        // Regression: Dataverse rejects FormXML when ANY <row> or <cell> is
        // missing the required `id` attribute (FormXmlRowCommon / FormXmlCellCommon
        // schema groups). This test asserts no <row> or <cell> escapes the
        // builder without a GUID id, including in header, body, subgrid tab,
        // and timeline tab.
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
            CancellationToken.None);

        // Lookahead `(?=[\s/>])` distinguishes the tag from the plural container
        // (e.g. `<row` vs `<rows>`, `<cell` vs `<cells>`, `<section` vs `<sections>`).
        // Every <row ...> must include id="..."
        var rowsWithoutId = System.Text.RegularExpressions.Regex.Matches(
            plan.FormXml, @"<row(?=[\s/>])(?![^>]*\bid=)[^>]*>");
        Assert.True(rowsWithoutId.Count == 0,
            $"Found {rowsWithoutId.Count} <row> element(s) without id attribute. FormXml:\n{plan.FormXml}");

        // Every <cell ...> must include id="..."
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
            CancellationToken.None);

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
            CancellationToken.None);

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
            CancellationToken.None);

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
            CancellationToken.None);

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
            CancellationToken.None);

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
            CancellationToken.None);

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
            CancellationToken.None);

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
            CancellationToken.None);

        Assert.Contains("formxml", plan.WebApiPayloadJson);
        Assert.Contains("objecttypecode", plan.WebApiPayloadJson);
        Assert.Contains("cr123_workorder", plan.WebApiPayloadJson);
    }
}
