using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateBusinessRuleTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a Dataverse business rule using a pre-built OData JSON payload from plan_create_business_rule. "
        + "Creates the rule, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateBusinessRuleAsync(
        [Description("The full OData JSON payload from plan_create_business_rule")] string webApiPayloadJson,
        [Description("The rule name")] string ruleName,
        [Description("The table logical name")] string tableLogicalName,
        CancellationToken cancellationToken)
    {
        var ruleId = await dataverseClient.CreateBusinessRuleAsync(webApiPayloadJson, cancellationToken);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.BusinessRules.Add(new CreatedBusinessRule(ruleId, ruleName, tableLogicalName, DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.AutomationBuilder,
            ToolName = "ExecuteCreateBusinessRule",
            Parameters = new { ruleName, tableLogicalName },
            Result = new { ruleId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteBusinessRule",
                Parameters = new { ruleId, tableLogicalName },
                Warning = "Deleting a business rule removes client-side validation for all users.",
                RiskLevel = RiskLevel.Medium
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"Business rule '{ruleName}' created on table '{tableLogicalName}' with RuleId={ruleId}.";
    }
}
