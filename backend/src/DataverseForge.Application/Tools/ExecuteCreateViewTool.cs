using DataverseForge.Application.Agents;
using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateViewTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateViewTool> logger)
{
    [Description("Execute the creation of a Dataverse system view. Pass the planId from plan_create_view — "
        + "the OData JSON payload (with embedded FetchXML and LayoutXML) was stored server-side and will "
        + "be loaded automatically. Do NOT try to construct or pass the payload yourself.")]
    public async Task<string> ExecuteCreateViewAsync(
        [Description("planId returned by plan_create_view — must match exactly. Payload is stored server-side; "
            + "do NOT construct the OData JSON yourself.")] string planId,
        [Description("The logical name of the table the view belongs to")] string tableLogicalName,
        [Description("The display name of the view")] string viewName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateView executing: planId={PlanId}, tableLogicalName={TableLogicalName}, viewName={ViewName}",
            planId, tableLogicalName, viewName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_view_invalid_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_view (likely missing columns or table). "
                + "Fix the input and re-plan; do NOT execute this planId.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteCreateView: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"View '{viewName}' is already on '{tableLogicalName}'. DO NOT retry, DO NOT re-plan. "
                + "Move on to the NEXT pending plan in this turn.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteCreateView: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_view first and pass its returned planId verbatim.";
        }

        try
        {
            var savedQueryId = await dataverseClient.CreateViewAsync(webApiPayloadJson, cancellationToken);
            state.ExecutedPlans[planId] = webApiPayloadJson;
            state.PendingPlans.Remove(planId);

            // Add the new view (savedquery) to the active solution. Best-effort.
            var solutionAddNote = string.Empty;
            if (!string.IsNullOrWhiteSpace(state.SolutionUniqueName))
            {
                try
                {
                    await dataverseClient.AddSolutionComponentAsync(savedQueryId, SolutionComponentType.SavedQuery, state.SolutionUniqueName, doNotIncludeSubcomponents: true, cancellationToken);
                    solutionAddNote = $" Added to solution '{state.SolutionUniqueName}'.";
                }
                catch (Exception solEx)
                {
                    logger.LogWarning(solEx, "AddSolutionComponent failed for view {ViewName} ({SavedQueryId}) on solution {Solution}",
                        viewName, savedQueryId, state.SolutionUniqueName);
                    solutionAddNote = $" WARNING: view created in env but could NOT be added to solution '{state.SolutionUniqueName}': {solEx.Message}.";
                }
            }
            else
            {
                solutionAddNote = " WARNING: no active solution selected — the view is in the default solution.";
            }

            state.Views.Add(new CreatedView(savedQueryId, tableLogicalName, viewName, DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "ExecuteCreateView",
                Parameters = new { tableLogicalName, viewName },
                Result = new { savedQueryId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteView",
                    Parameters = new { savedQueryId, tableLogicalName },
                    Warning = "Deleting a view removes it from all model-driven apps that reference it.",
                    RiskLevel = RiskLevel.Medium
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateView",
                Detail = $"Created view '{viewName}' on table '{tableLogicalName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateView succeeded: savedQueryId={SavedQueryId}, viewName={ViewName}", savedQueryId, viewName);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateView"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"View '{viewName}' created on table '{tableLogicalName}' with SavedQueryId={savedQueryId}.{solutionAddNote} "
                + "NEXT STEP: call handoff_to_tester now to verify the view in Dataverse — do NOT reply to the user yet.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateView failed: tableLogicalName={TableLogicalName}, viewName={ViewName}", tableLogicalName, viewName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateView",
                Detail = $"Failed to create view '{viewName}' on table '{tableLogicalName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateView"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create view '{viewName}' on table '{tableLogicalName}': {ex.Message}. "
                + "Check that the table exists and the payload is valid. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
