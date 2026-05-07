using DataverseForge.Application.Agents;
using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateTableTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateTableTool> logger)
{
    [Description("Execute the creation of a Dataverse table. Pass the planId from plan_create_table — "
        + "the OData JSON payload was stored server-side and will be loaded automatically. "
        + "Do NOT try to construct the payload yourself.")]
    public async Task<string> ExecuteCreateTableAsync(
        [Description("planId returned by plan_create_table — must match exactly")] string planId,
        [Description("Logical name of the table being created (from plan_create_table.tableLogicalName)")] string tableLogicalName,
        [Description("Display name of the table (from plan_create_table.displayName)")] string displayName,
        [Description("Solution unique name (from plan_create_table.solutionUniqueName)")] string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateTable executing: planId={PlanId}, table={TableLogicalName}, solution={Solution}",
            planId, tableLogicalName, solutionUniqueName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteCreateTable: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier in this session. "
                + $"Table '{tableLogicalName}' is already in Dataverse and added to solution '{solutionUniqueName}'. "
                + "DO NOT retry, DO NOT re-plan. Move on to the NEXT pending plan — if the user just "
                + "approved a different operation (e.g. a Customer/Polymorphic lookup or a field), "
                + "execute THAT planId, not this one.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteCreateTable: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_table first and pass its returned planId verbatim.";
        }

        try
        {
            var metadataId = await dataverseClient.CreateTableAsync(webApiPayloadJson, cancellationToken);

            await dataverseClient.AddSolutionComponentAsync(metadataId, SolutionComponentType.Entity, solutionUniqueName, cancellationToken: cancellationToken);

            // F-23: re-read state right before mutate+save so concurrent writes
            // landed during the multi-second Dataverse work above survive.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.Tables.Add(new CreatedTable(metadataId, tableLogicalName.ToLowerInvariant(), displayName, DateTime.UtcNow));
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    ToolName = "ExecuteCreateTable",
                    Parameters = new { tableLogicalName, displayName, solutionUniqueName },
                    Result = new { metadataId },
                    IsReversible = true,
                    ReverseOp = new ReverseOperation
                    {
                        ToolName = "DeleteTable",
                        Parameters = new { metadataId, tableLogicalName },
                        Warning = "Deleting a table removes all data and dependent components.",
                        RiskLevel = RiskLevel.High
                    }
                });
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    Action = "ExecuteCreateTable",
                    Detail = $"Created table '{displayName}' ({tableLogicalName}) in solution '{solutionUniqueName}'",
                    Success = true
                });
            }, cancellationToken);

            logger.LogInformation("ExecuteCreateTable succeeded: metadataId={MetadataId}, tableLogicalName={TableLogicalName}", metadataId, tableLogicalName);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateTable"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Table '{displayName}' ({tableLogicalName.ToLowerInvariant()}) created successfully with MetadataId={metadataId} and added to solution '{solutionUniqueName}'. "
                + $"NEXT REQUIRED ACTION: call handoff_to_tester with summary including planId={planId} so the Tester can verify against the plan.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExecuteCreateTable failed: tableLogicalName={TableLogicalName}", tableLogicalName);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    Action = "ExecuteCreateTable",
                    Detail = $"Failed to create table '{tableLogicalName}': {ex.Message}",
                    Success = false
                });
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateTable"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create table '{displayName}' ({tableLogicalName}): {ex.Message}";
        }
    }
}
