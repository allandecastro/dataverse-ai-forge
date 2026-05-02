using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateFormTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateFormTool> logger)
{
    [Description("Execute the creation of a Dataverse form. Pass the planId from plan_create_form — "
        + "the OData JSON payload (with embedded FormXML) was stored server-side and will be loaded "
        + "automatically. Do NOT try to construct or pass the payload yourself.")]
    public async Task<string> ExecuteCreateFormAsync(
        [Description("planId returned by plan_create_form — must match exactly. Payload is stored server-side; "
            + "do NOT construct the OData JSON yourself.")] string planId,
        [Description("The logical name of the table the form belongs to")] string tableLogicalName,
        [Description("The display name of the form")] string formName,
        [Description("The form type: 'main', 'quickCreate', or 'quickView'")] string formType,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateForm executing: planId={PlanId}, tableLogicalName={TableLogicalName}, formName={FormName}, formType={FormType}",
            planId, tableLogicalName, formName, formType);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_form_invalid_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_form (likely missing fields). "
                + "Fix the field list and re-plan; do NOT execute this planId.";
        }

        if (!state.PendingPlans.TryGetValue(planId, out var webApiPayloadJson))
        {
            logger.LogWarning("ExecuteCreateForm: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_form first and pass its returned planId verbatim.";
        }

        try
        {
            var formId = await dataverseClient.CreateFormAsync(webApiPayloadJson, cancellationToken);
            state.ExecutedPlans[planId] = webApiPayloadJson;
            state.PendingPlans.Remove(planId);

            state.Forms.Add(new CreatedForm(formId, tableLogicalName, formName, formType, DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "ExecuteCreateForm",
                Parameters = new { tableLogicalName, formName, formType },
                Result = new { formId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteForm",
                    Parameters = new { formId, tableLogicalName },
                    Warning = "Deleting a form removes its layout. Users assigned to this form will fall back to the default.",
                    RiskLevel = RiskLevel.Medium
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateForm",
                Detail = $"Created form '{formName}' ({formType}) on table '{tableLogicalName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateForm succeeded: formId={FormId}, formName={FormName}", formId, formName);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateForm"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Form '{formName}' ({formType}) created on table '{tableLogicalName}' with FormId={formId}.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateForm failed: tableLogicalName={TableLogicalName}, formName={FormName}", tableLogicalName, formName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateForm",
                Detail = $"Failed to create form '{formName}' on table '{tableLogicalName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateForm"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create form '{formName}' ({formType}) on table '{tableLogicalName}': {ex.Message}. "
                + "Check that the table exists and the payload is valid. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
