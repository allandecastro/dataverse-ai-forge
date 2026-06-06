using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateAlternateKeyTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateAlternateKeyTool> logger)
{
    [Description("Execute creation of an alternate key. Pass the planId from plan_create_alternate_key — "
        + "the OData JSON payload was stored server-side and is loaded automatically. Do NOT try to "
        + "construct or pass the payload yourself.")]
    public async Task<string> ExecuteCreateAlternateKeyAsync(
        [Description("planId returned by plan_create_alternate_key — must match exactly. Payload is stored "
            + "server-side; do NOT construct the OData JSON yourself.")] string planId,
        [Description("The logical name of the table the key lives on (for audit + the Web API URL)")] string tableLogicalName,
        [Description("The logical name of the key being created (for audit/logging)")] string keyLogicalName,
        [Description("The display name of the key (for audit/logging)")] string keyDisplayName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var t = tableLogicalName.ToLowerInvariant();
        logger.LogInformation("ExecuteCreateAlternateKey executing: planId={PlanId}, table={Table}, key={Key}",
            planId, t, keyLogicalName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_alt_key_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_alternate_key (missing table, "
                + "missing attribute, or duplicate key). Fix the input and re-plan; do NOT execute this planId.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteCreateAlternateKey: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"Alternate key '{keyLogicalName}' is already on '{t}'. DO NOT retry, DO NOT re-plan. "
                + "Move on to the NEXT pending plan in this turn.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteCreateAlternateKey: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_alternate_key first and pass its returned planId verbatim.";
        }

        var solutionUniqueName = state.SolutionUniqueName; // capture before await

        try
        {
            var metadataId = await dataverseClient.CreateAlternateKeyAsync(t, webApiPayloadJson, cancellationToken);

            // Add the new alt key (EntityKey component type) to the active solution.
            // Best-effort.
            var solutionAddNote = string.Empty;
            if (!string.IsNullOrWhiteSpace(solutionUniqueName))
            {
                try
                {
                    await dataverseClient.AddSolutionComponentAsync(metadataId, SolutionComponentType.EntityKey, solutionUniqueName, doNotIncludeSubcomponents: true, cancellationToken);
                    solutionAddNote = $" Added to solution '{solutionUniqueName}'.";
                }
                catch (Exception solEx)
                {
                    logger.LogWarning(solEx, "AddSolutionComponent failed for alt key {KeyLogical} ({MetadataId}) on solution {Solution}",
                        keyLogicalName, metadataId, solutionUniqueName);
                    solutionAddNote = $" WARNING: alt key created in env but could NOT be added to solution '{solutionUniqueName}': {solEx.Message}.";
                }
            }
            else
            {
                solutionAddNote = " WARNING: no active solution selected — the alt key is in the default solution.";
            }

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.AlternateKeys.Add(new CreatedAlternateKey(metadataId, t, keyLogicalName, keyDisplayName, DateTime.UtcNow));
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    ToolName = "ExecuteCreateAlternateKey",
                    Parameters = new { tableLogicalName = t, keyLogicalName, keyDisplayName },
                    Result = new { metadataId, tableLogicalName = t },
                    IsReversible = true,
                    ReverseOp = new ReverseOperation
                    {
                        ToolName = "DeleteAlternateKey",
                        Parameters = new { tableLogicalName = t, metadataId, keyLogicalName },
                        Warning = "Deleting an alternate key removes the uniqueness constraint. Upsert operations referencing this key will fail.",
                        RiskLevel = RiskLevel.Medium
                    }
                });
                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.DataModelBuilder, "ExecuteCreateAlternateKey", $"Created alternate key '{keyDisplayName}' ({keyLogicalName}) on table '{t}'"));
            }, cancellationToken);

            logger.LogInformation("ExecuteCreateAlternateKey succeeded: metadataId={MetadataId}, key={Key}", metadataId, keyLogicalName);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteCreateAlternateKey", "success", sw.ElapsedMilliseconds);
            return $"Alternate key '{keyDisplayName}' ({keyLogicalName}) created on table '{t}' with MetadataId={metadataId}.{solutionAddNote} "
                + "NEXT REQUIRED ACTION: call handoff_to_tester so the Tester can verify the alternate key.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateAlternateKey failed: table={Table}, key={Key}", t, keyLogicalName);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.DataModelBuilder, "ExecuteCreateAlternateKey", $"Failed to create alternate key '{keyLogicalName}' on table '{t}': {ex.Message}"));
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteCreateAlternateKey", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to create alternate key '{keyDisplayName}' ({keyLogicalName}) on table '{t}': {ex.Message}. "
                + "Common causes: a key with the same logical name already exists, or one of the key attributes "
                + "isn't valid for an index (e.g., Memo, File). You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
