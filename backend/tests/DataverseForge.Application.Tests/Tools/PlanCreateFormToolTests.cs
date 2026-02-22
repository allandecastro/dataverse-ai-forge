using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateFormToolTests
{
    private readonly PlanCreateFormTool _tool = new();

    [Fact]
    public async Task PlanCreateForm_MainForm_ReturnsValidPlan()
    {
        var plan = await _tool.PlanCreateFormAsync(
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
        var plan = await _tool.PlanCreateFormAsync(
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
    public async Task PlanCreateForm_QuickCreate_WarnsOnTooManyFields()
    {
        var plan = await _tool.PlanCreateFormAsync(
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
        var plan = await _tool.PlanCreateFormAsync(
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
        var plan = await _tool.PlanCreateFormAsync(
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
        var plan = await _tool.PlanCreateFormAsync(
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
        var plan = await _tool.PlanCreateFormAsync(
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
        var plan = await _tool.PlanCreateFormAsync(
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
