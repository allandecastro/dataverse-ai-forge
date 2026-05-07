using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteUpdateFormTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteUpdateFormTool> logger)
{
    [Description("Execute an update to an existing Dataverse form. Pass the planId from plan_update_form — "
        + "the OData JSON payload was stored server-side and will be loaded automatically. Do NOT try to "
        + "construct or pass the payload yourself.")]
    public async Task<string> ExecuteUpdateFormAsync(
        [Description("planId returned by plan_update_form — must match exactly. Payload is stored server-side; "
            + "do NOT construct the OData JSON yourself.")] string planId,
        [Description("The GUID of the form being updated (from get_forms_list)")] string formId,
        [Description("The logical name of the table the form belongs to")] string tableLogicalName,
        [Description("The display name of the form (used for logging/audit)")] string formName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteUpdateForm executing: planId={PlanId}, formId={FormId}, tableLogicalName={TableLogicalName}",
            planId, formId, tableLogicalName);

        if (!Guid.TryParse(formId, out var parsedFormId) || parsedFormId == Guid.Empty)
        {
            return "ERROR: formId must be a valid GUID — call get_forms_list to retrieve it.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_form_invalid_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_update_form. "
                + "Fix the input and re-plan; do NOT execute this planId.";
        }

        // F-26: standard 3-arm planId resolution. Without this, an agent
        // re-firing Execute on the same planId (the loop motivated the 3-arm
        // lookup elsewhere) sees "not found" and tries to re-plan. CLAUDE.md
        // routing/idempotency: every Execute_* should use LookupPlan.
        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"Form '{formName}' (id {parsedFormId}) was already updated. DO NOT retry, DO NOT re-plan.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteUpdateForm: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_update_form first and pass its returned planId verbatim.";
        }

        try
        {
            await dataverseClient.UpdateFormAsync(parsedFormId, webApiPayloadJson, cancellationToken);

            // F-30: publish so the updated layout takes effect immediately.
            // Without this, end-users keep seeing the prior layout until
            // somebody runs PublishAll. Best-effort.
            string publishNote = string.Empty;
            try
            {
                await dataverseClient.PublishAllAsync(cancellationToken);
            }
            catch (Exception pubEx) when (pubEx is not OperationCanceledException)
            {
                logger.LogWarning(pubEx, "PublishAll after form update failed for {FormName} ({FormId})", formName, parsedFormId);
                publishNote = $" (publish all failed: {pubEx.Message} — admin can publish manually)";
            }

            // F-23: re-read state right before mutate+save.
            // Form updates are not pushed to the Forms list (it tracks creates).
            // Reverse op: not trivially undoable — we'd need the prior FormXML
            // snapshot. Mark as non-reversible for now; a future enhancement
            // could snapshot the existing form before update for true undo.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    ToolName = "ExecuteUpdateForm",
                    Parameters = new { formId, tableLogicalName, formName },
                    Result = new { formId },
                    IsReversible = false,
                    ReverseOp = null
                });
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "ExecuteUpdateForm",
                    Detail = $"Updated form '{formName}' (formId={parsedFormId}) on table '{tableLogicalName}'",
                    Success = true
                });
            }, cancellationToken);

            logger.LogInformation("ExecuteUpdateForm succeeded: formId={FormId}, formName={FormName}", parsedFormId, formName);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteUpdateForm"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Form '{formName}' (formId={parsedFormId}) updated on table '{tableLogicalName}'.{publishNote}";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteUpdateForm failed: formId={FormId}, formName={FormName}", parsedFormId, formName);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "ExecuteUpdateForm",
                    Detail = $"Failed to update form '{formName}' (formId={parsedFormId}) on table '{tableLogicalName}': {ex.Message}",
                    Success = false
                });
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteUpdateForm"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to update form '{formName}' (formId={parsedFormId}) on table '{tableLogicalName}': {ex.Message}. "
                + "Check that the form exists, the payload is valid, and you have permission. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
