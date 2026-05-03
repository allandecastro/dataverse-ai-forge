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
/// Removes a field from a SOLUTION (the field stays in the environment, just no
/// longer appears as a component of the named solution). Non-destructive — pair
/// with delete_field if the user wants the field gone from the environment.
/// Uses RemoveSolutionComponent under the hood with componentType=Attribute (2).
/// </summary>
public class RemoveFieldFromSolutionTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<RemoveFieldFromSolutionTool> logger)
{
    [Description("Remove a field from a SOLUTION (does NOT delete from the environment — the field "
        + "still exists, it just no longer appears as a component of this solution). Use this when "
        + "the user wants to clean up a solution's component list without losing the field or its "
        + "data. For full deletion across the environment, use delete_field instead. "
        + "Call request_approval before this tool — solution membership changes are visible to anyone "
        + "with the solution.")]
    public async Task<string> RemoveFieldFromSolutionAsync(
        [Description("The logical name of the table the field belongs to (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("The logical name of the field to remove (e.g., 'eppc_dategiven')")] string fieldLogicalName,
        [Description("The unique name of the solution to remove the field from (e.g., 'EPPCLiveDemo'). "
            + "Defaults to the active solution if empty.")] string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var t = tableLogicalName.ToLowerInvariant();
        var f = fieldLogicalName.ToLowerInvariant();

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var solution = string.IsNullOrWhiteSpace(solutionUniqueName)
            ? state.SolutionUniqueName
            : solutionUniqueName;

        if (string.IsNullOrWhiteSpace(solution))
        {
            return "ERROR: No solution specified and no active solution is set. Pass solutionUniqueName explicitly "
                + "or call select_solution first.";
        }

        logger.LogInformation("RemoveFieldFromSolution executing: table={Table}, field={Field}, solution={Solution}",
            t, f, solution);

        try
        {
            // Resolve the field's MetadataId — RemoveSolutionComponent needs the GUID.
            var meta = await dataverseClient.GetFieldMetadataAsync(t, f, cancellationToken);

            await dataverseClient.RemoveSolutionComponentAsync(
                meta.MetadataId,
                SolutionComponentType.Attribute,
                solution,
                cancellationToken);

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "RemoveFieldFromSolution",
                Parameters = new { tableLogicalName = t, fieldLogicalName = f, solutionUniqueName = solution },
                Result = new { tableLogicalName = t, fieldLogicalName = f, solutionUniqueName = solution, fieldMetadataId = meta.MetadataId },
                IsReversible = false,
                ReverseOp = null
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "RemoveFieldFromSolution",
                Detail = $"Removed field '{f}' on '{t}' from solution '{solution}' (field still exists in the environment)",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("RemoveFieldFromSolution succeeded: table={Table}, field={Field}, solution={Solution}",
                t, f, solution);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "RemoveFieldFromSolution"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Field '{f}' on table '{t}' removed from solution '{solution}'. The field still exists in the environment.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "RemoveFieldFromSolution failed: table={Table}, field={Field}, solution={Solution}",
                t, f, solution);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "RemoveFieldFromSolution",
                Detail = $"Failed to remove field '{f}' from solution '{solution}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "RemoveFieldFromSolution"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to remove field '{f}' on table '{t}' from solution '{solution}': {ex.Message}. "
                + "Check that the field is actually a component of that solution and that the solution is unmanaged.";
        }
    }
}
