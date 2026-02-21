using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanModifyFieldToolTests
{
    private readonly PlanModifyFieldTool _tool = new();

    [Fact]
    public async Task PlanModifyField_DisplayName_ReturnsModification()
    {
        var plan = await _tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_description",
            modificationsJson: "{\"displayName\":\"Project Description\"}",
            currentMaxLength: 0,
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Single(plan.Modifications);
        Assert.Equal("DisplayName", plan.Modifications[0].Property);
        Assert.Equal("Project Description", plan.Modifications[0].NewValue);
        Assert.Contains("DisplayName", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanModifyField_RequiredLevel_ApplicationRequired_ReturnsWarning()
    {
        var plan = await _tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_status",
            modificationsJson: "{\"requiredLevel\":\"ApplicationRequired\"}",
            currentMaxLength: 0,
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("ApplicationRequired"));
    }

    [Fact]
    public async Task PlanModifyField_DecreaseMaxLength_ReturnsWarning()
    {
        var plan = await _tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"maxLength\":100}",
            currentMaxLength: 200,
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("Decreasing MaxLength"));
    }

    [Fact]
    public async Task PlanModifyField_MultipleModifications_ReturnsAll()
    {
        var plan = await _tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"displayName\":\"Project Name\",\"isSearchable\":true,\"isAuditEnabled\":true}",
            currentMaxLength: 0,
            CancellationToken.None);

        Assert.Equal(3, plan.Modifications.Count);
    }

    [Fact]
    public async Task PlanModifyField_InvalidProperty_ReturnsWarning()
    {
        var plan = await _tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"fieldType\":\"integer\"}",
            currentMaxLength: 0,
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("cannot be modified"));
    }

    [Fact]
    public async Task PlanModifyField_RiskLevel_IsYellow()
    {
        var plan = await _tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"displayName\":\"New Name\"}",
            currentMaxLength: 0,
            CancellationToken.None);

        Assert.Equal("yellow", plan.RiskLevel);
    }
}
