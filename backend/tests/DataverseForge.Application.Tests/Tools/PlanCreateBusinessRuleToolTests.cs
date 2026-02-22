using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateBusinessRuleToolTests
{
    private readonly PlanCreateBusinessRuleTool _tool = new();

    [Fact]
    public async Task PlanCreateBusinessRule_ValidInput_ReturnsPlan()
    {
        var plan = await _tool.PlanCreateBusinessRuleAsync(
            tableLogicalName: "cr123_workorder",
            ruleName: "Set Priority Required When Active",
            description: "Makes priority required when status is active",
            scope: "Entity",
            conditionsDefinition: "statecode:eq:0:and",
            actionsDefinition: "setRequired:cr123_priority:true",
            elseActionsDefinition: "",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("cr123_workorder", plan.TableLogicalName);
        Assert.Equal("Set Priority Required When Active", plan.RuleName);
        Assert.Equal("Makes priority required when status is active", plan.Description);
        Assert.Equal("Entity", plan.Scope);
        Assert.Single(plan.Conditions);
        Assert.Single(plan.Actions);
        Assert.Empty(plan.ElseActions);
        Assert.NotEmpty(plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateBusinessRule_EmptyConditions_Warning()
    {
        var plan = await _tool.PlanCreateBusinessRuleAsync(
            tableLogicalName: "cr123_workorder",
            ruleName: "Always Set Default",
            description: "Rule with no conditions",
            scope: "Entity",
            conditionsDefinition: "",
            actionsDefinition: "setValue:cr123_priority:1",
            elseActionsDefinition: "",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("No conditions"));
    }

    [Fact]
    public async Task PlanCreateBusinessRule_EmptyActions_Warning()
    {
        var plan = await _tool.PlanCreateBusinessRuleAsync(
            tableLogicalName: "cr123_workorder",
            ruleName: "No Effect Rule",
            description: "Rule with no actions",
            scope: "Entity",
            conditionsDefinition: "statecode:eq:0:and",
            actionsDefinition: "",
            elseActionsDefinition: "",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("No actions"));
    }

    [Fact]
    public async Task PlanCreateBusinessRule_InvalidActionType_Warning()
    {
        var plan = await _tool.PlanCreateBusinessRuleAsync(
            tableLogicalName: "cr123_workorder",
            ruleName: "Bad Action Rule",
            description: "Rule with invalid action type",
            scope: "Entity",
            conditionsDefinition: "statecode:eq:0:and",
            actionsDefinition: "invalidAction:cr123_priority:true",
            elseActionsDefinition: "",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("invalidAction") && w.Contains("not standard"));
    }

    [Fact]
    public async Task PlanCreateBusinessRule_AlwaysWarnsClientSideOnly()
    {
        var plan = await _tool.PlanCreateBusinessRuleAsync(
            tableLogicalName: "cr123_workorder",
            ruleName: "Any Rule",
            description: "Any rule should warn about client-side",
            scope: "Entity",
            conditionsDefinition: "statecode:eq:0:and",
            actionsDefinition: "setRequired:cr123_priority:true",
            elseActionsDefinition: "",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("CLIENT-SIDE only"));
    }

    [Fact]
    public async Task PlanCreateBusinessRule_PayloadContainsCategory2()
    {
        var plan = await _tool.PlanCreateBusinessRuleAsync(
            tableLogicalName: "cr123_workorder",
            ruleName: "Category Check Rule",
            description: "Payload should contain category 2",
            scope: "Entity",
            conditionsDefinition: "statecode:eq:0:and",
            actionsDefinition: "setRequired:cr123_priority:true",
            elseActionsDefinition: "",
            CancellationToken.None);

        Assert.Contains("\"category\": 2", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateBusinessRule_RiskLevelIsYellow()
    {
        var plan = await _tool.PlanCreateBusinessRuleAsync(
            tableLogicalName: "cr123_workorder",
            ruleName: "Risk Level Rule",
            description: "Risk level should be yellow",
            scope: "Entity",
            conditionsDefinition: "statecode:eq:0:and",
            actionsDefinition: "setRequired:cr123_priority:true",
            elseActionsDefinition: "",
            CancellationToken.None);

        Assert.Equal("yellow", plan.RiskLevel);
    }
}
