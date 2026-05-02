using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateGlobalOptionSetTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateGlobalOptionSetTool> logger)
{
    [Description("Execute the creation of a global option set in Dataverse. Pass the planId from "
        + "plan_create_global_option_set — the OData JSON payload was stored server-side and will be "
        + "loaded automatically. Do NOT try to construct the payload yourself.")]
    public async Task<string> ExecuteCreateGlobalOptionSetAsync(
        [Description("planId returned by plan_create_global_option_set — must match exactly")] string planId,
        [Description("The schema name of the global option set (lowercase, e.g. 'eppc_type')")] string optionSetName,
        [Description("The solution unique name to add the option set to")] string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateGlobalOptionSet executing: planId={PlanId}, optionSetName={OptionSetName}, solution={Solution}",
            planId, optionSetName, solutionUniqueName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        // Plans flagged as already-existing by plan_create_global_option_set must
        // never be executed. Returning a SKIPPED result keeps the agent from
        // looping on a plan that has no payload and no work to do.
        if (planId.StartsWith("rejected_optionset_exists_", StringComparison.Ordinal))
        {
            logger.LogInformation("ExecuteCreateGlobalOptionSet: skipping rejected (already-exists) plan: {PlanId}", planId);
            return $"SKIPPED: Plan '{planId}' was rejected because the global option set "
                + $"'{optionSetName}' already exists. Treat the requirement as satisfied and "
                + "DO NOT retry. If a Choice field needs this option set, plan_create_field uses "
                + "the existing one automatically.";
        }

        if (!state.PendingPlans.TryGetValue(planId, out var webApiPayloadJson))
        {
            logger.LogWarning("ExecuteCreateGlobalOptionSet: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_global_option_set first and pass its returned planId verbatim.";
        }

        try
        {
            var metadataId = await dataverseClient.CreateGlobalOptionSetAsync(webApiPayloadJson, cancellationToken);
            state.ExecutedPlans[planId] = webApiPayloadJson;
            state.PendingPlans.Remove(planId);

            await dataverseClient.AddSolutionComponentAsync(metadataId, SolutionComponentType.OptionSet, solutionUniqueName, cancellationToken: cancellationToken);

            state.OptionSets.Add(new CreatedOptionSet(metadataId, optionSetName, IsGlobal: true, DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "ExecuteCreateGlobalOptionSet",
                Parameters = new { optionSetName, solutionUniqueName },
                Result = new { metadataId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteGlobalOptionSet",
                    Parameters = new { metadataId, optionSetName },
                    Warning = "Deleting a global option set removes it from all fields that reference it.",
                    RiskLevel = RiskLevel.High
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreateGlobalOptionSet",
                Detail = $"Created global option set '{optionSetName}' in solution '{solutionUniqueName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateGlobalOptionSet succeeded: metadataId={MetadataId}, optionSetName={OptionSetName}",
                metadataId, optionSetName);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateGlobalOptionSet"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Global option set '{optionSetName}' created with MetadataId={metadataId} and added to solution '{solutionUniqueName}'. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester so the Tester can verify the option set.";
        }
        catch (HttpRequestException ex)
        {
            // Idempotency: if Dataverse rejects the create because the option set
            // already exists, treat the plan as satisfied. Clear it from
            // PendingPlans so the agent stops looping trying to "discard" it.
            // Without this, the trailing pending plan blocks the entry-agent
            // guard and the DMB spirals trying to clean up state that no tool
            // can actually clean.
            var msg = ex.Message ?? string.Empty;
            var alreadyExists = msg.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("0x80060832", StringComparison.OrdinalIgnoreCase) // Dataverse "duplicate metadata id" code
                || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase);

            if (alreadyExists)
            {
                logger.LogInformation("ExecuteCreateGlobalOptionSet: option set '{OptionSetName}' already exists; clearing plan and returning SKIPPED", optionSetName);
                state.PendingPlans.Remove(planId);
                state.ExecutedPlans[planId] = webApiPayloadJson;
                state.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    Action = "ExecuteCreateGlobalOptionSet",
                    Detail = $"Skipped — global option set '{optionSetName}' already exists in environment",
                    Success = true
                });
                await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
                sw.Stop();
                ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateGlobalOptionSet"), new KeyValuePair<string, object?>("tool.status", "skipped"));
                return $"SKIPPED: Global option set '{optionSetName}' already exists in this environment. "
                    + "The plan has been marked complete; treat the requirement as satisfied. "
                    + "DO NOT retry, DO NOT try to discard the plan, and DO NOT hand off to Solution Architect for cleanup.";
            }

            logger.LogError(ex, "ExecuteCreateGlobalOptionSet failed: optionSetName={OptionSetName}", optionSetName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreateGlobalOptionSet",
                Detail = $"Failed to create global option set '{optionSetName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateGlobalOptionSet"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create global option set '{optionSetName}': {ex.Message}. Check payload and environment. You may retry or hand off to Solution Architect.";
        }
    }
}
