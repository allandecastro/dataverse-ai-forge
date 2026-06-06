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
/// Reactivates a previously deactivated alternate key via the
/// <c>ReactivateEntityKey</c> action. Per the Dataverse doc, the action queues
/// an async job to rebuild the SQL index — the HTTP call returns immediately,
/// but the key isn't usable for Upsert until the index job completes (typically
/// seconds to minutes for small tables, longer for large ones).
/// </summary>
public class ReactivateAlternateKeyTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ReactivateAlternateKeyTool> logger)
{
    [Description("Reactivate a previously deactivated alternate key. Queues an async job to rebuild "
        + "the SQL index — the call returns fast but the key isn't usable for Upsert-by-key until the "
        + "index job finishes (seconds → minutes depending on table size). The agent should NOT loop "
        + "verifying immediately; tell the user the rebuild is queued and they can check in the maker "
        + "portal under the table's Keys tab.")]
    public async Task<string> ReactivateAlternateKeyAsync(
        [Description("Logical name of the table the alternate key lives on (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("Logical name of the alternate key (e.g., 'eppc_externalgiftid_key')")] string keyLogicalName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var t = tableLogicalName.ToLowerInvariant();
        var k = keyLogicalName.ToLowerInvariant();
        logger.LogInformation("ReactivateAlternateKey executing: table={Table}, key={Key}", t, k);

        try
        {
            await dataverseClient.ReactivateAlternateKeyAsync(t, k, cancellationToken);

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    ToolName = "ReactivateAlternateKey",
                    Parameters = new { tableLogicalName = t, keyLogicalName = k },
                    Result = new { tableLogicalName = t, keyLogicalName = k },
                    IsReversible = true,
                    ReverseOp = new ReverseOperation
                    {
                        ToolName = "DeactivateAlternateKey",
                        Parameters = new { tableLogicalName = t, keyLogicalName = k },
                        Warning = "Re-deactivates the key. Index stays but uniqueness stops being enforced.",
                        RiskLevel = RiskLevel.Low
                    }
                });

                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.DataModelBuilder, "ReactivateAlternateKey", $"Reactivated alternate key '{k}' on table '{t}' (async index rebuild queued)"));
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ReactivateAlternateKey", "success", sw.ElapsedMilliseconds);
            return $"Alternate key '{k}' on table '{t}' is being REACTIVATED. Dataverse queued an async "
                + "system job to rebuild the SQL index — the key won't accept Upsert-by-key calls until "
                + "the rebuild completes (seconds for small tables, minutes for large). Check status in "
                + "the maker portal: Tables → " + t + " → Keys.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ReactivateAlternateKey failed: table={Table}, key={Key}", t, k);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.DataModelBuilder, "ReactivateAlternateKey", $"Failed to reactivate alternate key '{k}' on '{t}': {ex.Message}"));
            }, cancellationToken);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ReactivateAlternateKey", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Could not reactivate alternate key '{k}' on '{t}': {ex.Message}. "
                + "Common causes: the key doesn't exist (use get_table_details.AlternateKeys to confirm) "
                + "or the key is already active.";
        }
    }
}
