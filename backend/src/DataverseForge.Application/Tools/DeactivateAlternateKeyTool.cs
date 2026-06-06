using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Deactivates an alternate key without deleting it via the
/// <c>DeactivateEntityKey</c> action. Use this when the user wants to pause
/// a uniqueness constraint (or unblock a misconfigured Upsert) while keeping
/// the key + its underlying SQL index in place. Distinct from
/// <see cref="DeleteAlternateKeyTool"/> — that fully removes the key.
/// </summary>
public class DeactivateAlternateKeyTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DeactivateAlternateKeyTool> logger)
{
    [Description("Deactivate an alternate key WITHOUT deleting it. The key, its definition, and its "
        + "underlying SQL index stay in place but Upsert-by-key calls fail and the uniqueness constraint "
        + "stops being enforced. Use this when the user wants to TEMPORARILY disable a key (paused upsert, "
        + "broken integration, mid-migration cleanup) — they can reactivate it later from the maker portal. "
        + "If the user wants the key GONE permanently, use delete_alternate_key instead. "
        + "Both operations should be preceded by request_approval — solution membership / index changes are "
        + "visible to other users.")]
    public async Task<string> DeactivateAlternateKeyAsync(
        [Description("Logical name of the table the alternate key lives on (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("Logical name of the alternate key (e.g., 'eppc_externalgiftid_key' — get from get_table_details.AlternateKeys)")] string keyLogicalName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var t = tableLogicalName.ToLowerInvariant();
        var k = keyLogicalName.ToLowerInvariant();
        logger.LogInformation("DeactivateAlternateKey executing: table={Table}, key={Key}", t, k);

        try
        {
            await dataverseClient.DeactivateAlternateKeyAsync(t, k, cancellationToken);

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                // Reverse op: ReactivateEntityKey rebuilds the SQL index asynchronously.
                // Round-trip is fully supported via the reactivate_alternate_key tool.
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    ToolName = "DeactivateAlternateKey",
                    Parameters = new { tableLogicalName = t, keyLogicalName = k },
                    Result = new { tableLogicalName = t, keyLogicalName = k },
                    IsReversible = true,
                    ReverseOp = new ReverseOperation
                    {
                        ToolName = "ReactivateAlternateKey",
                        Parameters = new { tableLogicalName = t, keyLogicalName = k },
                        Warning = "Reactivates the alternate key. Dataverse rebuilds the SQL index "
                            + "asynchronously — the key isn't usable for Upsert until the rebuild completes.",
                        RiskLevel = RiskLevel.Low
                    }
                });

                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.DataModelBuilder, "DeactivateAlternateKey", $"Deactivated alternate key '{k}' on table '{t}'"));
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("DeactivateAlternateKey", "success", sw.ElapsedMilliseconds);
            return $"Alternate key '{k}' on table '{t}' is now DEACTIVATED. The key + index remain; "
                + "uniqueness is no longer enforced and Upsert-by-key calls will fail. Call "
                + "reactivate_alternate_key when you're ready to bring it back online (Dataverse "
                + "rebuilds the index asynchronously). If the goal was permanent removal, call "
                + "delete_alternate_key instead.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "DeactivateAlternateKey failed: table={Table}, key={Key}", t, k);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.DataModelBuilder, "DeactivateAlternateKey", $"Failed to deactivate alternate key '{k}' on '{t}': {ex.Message}"));
            }, cancellationToken);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("DeactivateAlternateKey", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Could not deactivate alternate key '{k}' on '{t}': {ex.Message}. "
                + "Common causes: the key doesn't exist (use get_table_details.AlternateKeys to confirm), "
                + "or the key is already inactive.";
        }
    }
}
