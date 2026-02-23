using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateBusinessRuleTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateBusinessRuleTool> logger)
{
    [Description("Execute the creation of a Dataverse business rule using a pre-built OData JSON payload from plan_create_business_rule. "
        + "Creates the rule, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateBusinessRuleAsync(
        [Description("The full OData JSON payload from plan_create_business_rule")] string webApiPayloadJson,
        [Description("The rule name")] string ruleName,
        [Description("The table logical name")] string tableLogicalName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateBusinessRule executing: ruleName={RuleName}, tableLogicalName={TableLogicalName}", ruleName, tableLogicalName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var ruleId = await dataverseClient.CreateBusinessRuleAsync(webApiPayloadJson, cancellationToken);

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

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                Action = "ExecuteCreateBusinessRule",
                Detail = $"Created business rule '{ruleName}' on table '{tableLogicalName}' with RuleId={ruleId}",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateBusinessRule succeeded: ruleId={RuleId}", ruleId);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateBusinessRule"), new KeyValuePair<string, object?>("tool.status", "success"));

            return $"Business rule '{ruleName}' created on table '{tableLogicalName}' with RuleId={ruleId}.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateBusinessRule failed: ruleName={RuleName}, tableLogicalName={TableLogicalName}", ruleName, tableLogicalName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                Action = "ExecuteCreateBusinessRule",
                Detail = $"Failed to create business rule '{ruleName}' on table '{tableLogicalName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateBusinessRule"), new KeyValuePair<string, object?>("tool.status", "error"));

            return $"ERROR: Failed to create business rule '{ruleName}' on table '{tableLogicalName}'. {ex.Message}. Check connectivity and payload format.";
        }
    }
}
