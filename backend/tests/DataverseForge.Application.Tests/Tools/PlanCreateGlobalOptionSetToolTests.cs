using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateGlobalOptionSetToolTests
{
    private readonly PlanCreateGlobalOptionSetTool _tool = new();

    [Fact]
    public async Task PlanGlobalOptionSet_ValidInput_ReturnsValidPlan()
    {
        var optionsJson = "[{\"value\":100000,\"label\":\"Low\"},{\"value\":100001,\"label\":\"Medium\"},{\"value\":100002,\"label\":\"High\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "cr123_priority",
            displayName: "Priority",
            description: "Priority levels",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("cr123_priority", plan.Name);
        Assert.Equal("Priority", plan.DisplayName);
        Assert.Equal(3, plan.Options.Count);
        Assert.Contains("Microsoft.Dynamics.CRM.OptionSetMetadata", plan.WebApiPayloadJson);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PlanGlobalOptionSet_NoOptions_ReturnsWarning()
    {
        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "cr123_empty",
            displayName: "Empty",
            description: "No options",
            optionSetType: "Picklist",
            optionsJson: "[]",
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("no options"));
    }

    [Fact]
    public async Task PlanGlobalOptionSet_MissingPrefix_ReturnsWarning()
    {
        var optionsJson = "[{\"value\":100000,\"label\":\"Yes\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "priority",
            displayName: "Priority",
            description: "Priority levels",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("prefix"));
    }

    [Fact]
    public async Task PlanGlobalOptionSet_RiskLevel_IsYellow()
    {
        var optionsJson = "[{\"value\":100000,\"label\":\"Yes\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "cr123_yesno",
            displayName: "Yes/No",
            description: "Simple choice",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.Equal("yellow", plan.RiskLevel);
    }
}
