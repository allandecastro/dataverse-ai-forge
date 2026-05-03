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
/// Direct delete of a Dataverse field (column) from the environment. DESTRUCTIVE:
/// the field and ALL data stored in it on every record are permanently removed.
/// The agent MUST call request_approval before invoking this tool. Reverse op is
/// not trivially possible — recreating the field gives a new MetadataId and does
/// not restore data. Pair with RemoveFieldFromSolutionTool when the user wants
/// to keep the field in the environment but pull it from a specific solution.
/// </summary>
public class DeleteFieldTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DeleteFieldTool> logger)
{
    [Description("Delete a Dataverse field (column) from the ENVIRONMENT. DESTRUCTIVE: removes the field "
        + "and all data stored in it across every record. Call request_approval BEFORE this tool. "
        + "If the user only wants to remove the field from a single solution (keep it in the env), "
        + "use remove_field_from_solution instead.")]
    public async Task<string> DeleteFieldAsync(
        [Description("The logical name of the table the field belongs to (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("The logical name of the field to delete (e.g., 'eppc_dategiven')")] string fieldLogicalName,
        [Description("MUST be true to actually delete. Pass true ONLY when the user explicitly said " +
            "'delete from the environment' / 'drop the column' / 'delete the field' with NO mention of " +
            "a solution. If the user said 'remove from the solution' / 'remove from <SolutionName>' / " +
            "'take it out of the solution', this is the WRONG tool — call remove_field_from_solution " +
            "and pass false here. The tool refuses when this is false to prevent accidental data loss.")] bool confirmDeleteFromEnvironment,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var t = tableLogicalName.ToLowerInvariant();
        var f = fieldLogicalName.ToLowerInvariant();

        if (!confirmDeleteFromEnvironment)
        {
            logger.LogWarning("DeleteField refused: confirmDeleteFromEnvironment=false (table={Table}, field={Field})", t, f);
            return "REFUSED: delete_field requires confirmDeleteFromEnvironment=true. If the user asked to "
                + "REMOVE THE FIELD FROM A SOLUTION (not delete from env), call remove_field_from_solution "
                + "instead — that keeps the column and its data intact and only unlinks it from the solution. "
                + "If the user really did mean a full environment delete (data is permanently lost), retry "
                + "with confirmDeleteFromEnvironment=true.";
        }

        logger.LogInformation("DeleteField executing: table={Table}, field={Field}", t, f);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            await dataverseClient.DeleteFieldByLogicalNameAsync(t, f, cancellationToken);

            // Drop from project state if we tracked it.
            state.Fields.RemoveAll(fld =>
                string.Equals(fld.TableLogicalName, t, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fld.LogicalName, f, StringComparison.OrdinalIgnoreCase));

            // Reverse op: not trivially undoable — recreating gives a new MetadataId
            // and zero data. Mark as non-reversible.
            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "DeleteField",
                Parameters = new { tableLogicalName = t, fieldLogicalName = f },
                Result = new { tableLogicalName = t, fieldLogicalName = f },
                IsReversible = false,
                ReverseOp = null
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "DeleteField",
                Detail = $"Deleted field '{f}' from table '{t}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("DeleteField succeeded: table={Table}, field={Field}", t, f);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "DeleteField"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Field '{f}' deleted from table '{t}'.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "DeleteField failed: table={Table}, field={Field}", t, f);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "DeleteField",
                Detail = $"Failed to delete field '{f}' on '{t}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "DeleteField"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to delete field '{f}' on table '{t}': {ex.Message}. "
                + "Common causes: field is system-managed (cannot be deleted), the field is referenced by "
                + "a relationship/business rule/form, or the table itself is part of a managed solution. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
