using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanAddOptionToOptionSetToolTests
{
    private readonly PlanAddOptionToOptionSetTool _tool = new();

    [Fact]
    public async Task PlanAddOption_GlobalOptionSet_ReturnsValidPlan()
    {
        var plan = await _tool.PlanAddOptionToOptionSetAsync(
            optionSetName: "cr123_priority",
            isGlobal: true,
            tableLogicalName: "",
            fieldLogicalName: "",
            newOptionValue: 100003,
            newOptionLabel: "Critical",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.True(plan.IsGlobal);
        Assert.Equal(100003, plan.NewOptionValue);
        Assert.Equal("Critical", plan.NewOptionLabel);
        Assert.Contains("OptionSetName", plan.WebApiPayloadJson);
        Assert.Contains("cr123_priority", plan.WebApiPayloadJson);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PlanAddOption_LocalOptionSet_MissingTable_ReturnsWarning()
    {
        var plan = await _tool.PlanAddOptionToOptionSetAsync(
            optionSetName: "local_status",
            isGlobal: false,
            tableLogicalName: "",
            fieldLogicalName: "",
            newOptionValue: 100000,
            newOptionLabel: "Active",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("Table logical name"));
    }

    [Fact]
    public async Task PlanAddOption_RiskLevel_IsGreen()
    {
        var plan = await _tool.PlanAddOptionToOptionSetAsync(
            optionSetName: "cr123_priority",
            isGlobal: true,
            tableLogicalName: "",
            fieldLogicalName: "",
            newOptionValue: 100003,
            newOptionLabel: "Critical",
            CancellationToken.None);

        Assert.Equal("green", plan.RiskLevel);
    }
}
