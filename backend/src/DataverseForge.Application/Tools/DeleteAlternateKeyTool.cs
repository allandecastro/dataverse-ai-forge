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
/// Direct delete of an alternate key. No plan/execute split — there's no
/// payload to mangle, just the table + key logical name. The agent MUST call
/// request_approval first (uniqueness constraint goes away; Upsert calls
/// referencing this key start failing). The MetadataId is resolved server-side
/// by looking the key up by logical name on the table.
/// </summary>
public class DeleteAlternateKeyTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DeleteAlternateKeyTool> logger)
{
    [Description("Delete an alternate key from a Dataverse table by logical name. DESTRUCTIVE: removes "
        + "the key, its definition, and the underlying SQL index. Breaks any Upsert calls referencing "
        + "this key. Call request_approval BEFORE this tool. The MetadataId is resolved internally — "
        + "pass the table and key logical names only. "
        + "ALTERNATIVE: if the user wants to TEMPORARILY disable the key (paused upsert, mid-migration "
        + "cleanup) rather than permanently remove it, use deactivate_alternate_key instead. That keeps "
        + "the key + index in place and is reversible from the maker portal.")]
    public async Task<string> DeleteAlternateKeyAsync(
        [Description("The logical name of the table the key lives on (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("The logical name of the alternate key to delete (e.g., 'eppc_giftcodekey'). "
            + "If unsure, list the table's keys first via get_table_details — alternate keys appear in "
            + "the AlternateKeys array.")] string keyLogicalName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var t = tableLogicalName.ToLowerInvariant();
        var k = keyLogicalName.ToLowerInvariant();
        logger.LogInformation("DeleteAlternateKey executing: table={Table}, key={Key}", t, k);

        try
        {
            // Resolve MetadataId by listing the table's keys (Dataverse only
            // accepts the GUID form on DELETE).
            var keys = await dataverseClient.GetAlternateKeysAsync(t, cancellationToken);
            var match = keys.FirstOrDefault(x =>
                string.Equals(x.LogicalName, k, StringComparison.OrdinalIgnoreCase));

            if (match is null || match.MetadataId == Guid.Empty)
            {
                var available = string.Join(", ", keys.Select(x => x.LogicalName).OrderBy(s => s));
                logger.LogWarning("DeleteAlternateKey: key not found on table {Table}, key={Key}", t, k);
                return $"ERROR: Alternate key '{k}' was not found on table '{t}'. "
                    + (string.IsNullOrEmpty(available)
                        ? "This table has no alternate keys."
                        : $"Available keys: {available}.");
            }

            await dataverseClient.DeleteAlternateKeyAsync(t, match.MetadataId, cancellationToken);

            var matchedMetadataId = match.MetadataId;
            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AlternateKeys.RemoveAll(ak =>
                    string.Equals(ak.TableLogicalName, t, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ak.KeyLogicalName, k, StringComparison.OrdinalIgnoreCase));
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    ToolName = "DeleteAlternateKey",
                    Parameters = new { tableLogicalName = t, keyLogicalName = k, metadataId = matchedMetadataId },
                    Result = new { tableLogicalName = t, keyLogicalName = k },
                    IsReversible = false,
                    ReverseOp = null
                });
                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.DataModelBuilder, "DeleteAlternateKey", $"Deleted alternate key '{k}' from table '{t}'"));
            }, cancellationToken);

            logger.LogInformation("DeleteAlternateKey succeeded: table={Table}, key={Key}, metadataId={MetadataId}",
                t, k, matchedMetadataId);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("DeleteAlternateKey", "success", sw.ElapsedMilliseconds);
            return $"Alternate key '{k}' deleted from table '{t}'.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "DeleteAlternateKey failed: table={Table}, key={Key}", t, k);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.DataModelBuilder, "DeleteAlternateKey", $"Failed to delete alternate key '{k}' on '{t}': {ex.Message}"));
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("DeleteAlternateKey", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to delete alternate key '{k}' on table '{t}': {ex.Message}";
        }
    }
}
