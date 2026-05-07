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

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteCreateForm: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"Form '{formName}' is already on '{tableLogicalName}'. DO NOT retry, DO NOT re-plan. "
                + "Move on to the NEXT pending plan in this turn.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteCreateForm: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_form first and pass its returned planId verbatim.";
        }

        var solutionUniqueName = state.SolutionUniqueName; // capture before await

        try
        {
            var formId = await dataverseClient.CreateFormAsync(webApiPayloadJson, cancellationToken);

            // Add the new form to the active solution. Forms aren't auto-included
            // when a table is in the solution with doNotIncludeSubcomponents=true.
            // Best-effort — log a warning if it fails; the form still exists in
            // the env and the user can add it manually.
            var solutionAddNote = string.Empty;
            if (!string.IsNullOrWhiteSpace(solutionUniqueName))
            {
                try
                {
                    await dataverseClient.AddSolutionComponentAsync(formId, SolutionComponentType.Form, solutionUniqueName, doNotIncludeSubcomponents: true, cancellationToken);
                    solutionAddNote = $" Added to solution '{solutionUniqueName}'.";
                }
                catch (Exception solEx)
                {
                    logger.LogWarning(solEx, "AddSolutionComponent failed for form {FormName} ({FormId}) on solution {Solution}",
                        formName, formId, solutionUniqueName);
                    solutionAddNote = $" WARNING: form created in env but could NOT be added to solution '{solutionUniqueName}': {solEx.Message}.";
                }
            }
            else
            {
                solutionAddNote = " WARNING: no active solution selected — the form is in the default solution.";
            }

            // F-30: publish so the new form renders in the maker portal /
            // model-driven app immediately. Without this, end-users see the
            // prior form layout until somebody runs PublishAll. Best-effort —
            // failed publish doesn't fail the create.
            try
            {
                await dataverseClient.PublishAllAsync(cancellationToken);
            }
            catch (Exception pubEx) when (pubEx is not OperationCanceledException)
            {
                logger.LogWarning(pubEx, "PublishAll after form create failed for {FormName} ({FormId})", formName, formId);
                solutionAddNote += $" (publish all failed: {pubEx.Message} — admin can publish manually).";
            }

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.Forms.Add(new CreatedForm(formId, tableLogicalName, formName, formType, DateTime.UtcNow));
                fresh.OperationStack.Push(new ExecutedOperation
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
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "ExecuteCreateForm",
                    Detail = $"Created form '{formName}' ({formType}) on table '{tableLogicalName}'",
                    Success = true
                });
            }, cancellationToken);

            logger.LogInformation("ExecuteCreateForm succeeded: formId={FormId}, formName={FormName}", formId, formName);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateForm"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Form '{formName}' ({formType}) created on table '{tableLogicalName}' with FormId={formId}.{solutionAddNote}";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateForm failed: tableLogicalName={TableLogicalName}, formName={FormName}", tableLogicalName, formName);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "ExecuteCreateForm",
                    Detail = $"Failed to create form '{formName}' on table '{tableLogicalName}': {ex.Message}",
                    Success = false
                });
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateForm"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create form '{formName}' ({formType}) on table '{tableLogicalName}': {ex.Message}. "
                + "Check that the table exists and the payload is valid. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
