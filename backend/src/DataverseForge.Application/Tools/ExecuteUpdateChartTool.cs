using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteUpdateChartTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteUpdateChartTool> logger)
{
    [Description("Execute the update of an existing Dataverse chart. Pass the planId from "
        + "plan_update_chart — the OData PATCH payload was stored server-side. "
        + "Do NOT construct the payload yourself.")]
    public async Task<string> ExecuteUpdateChartAsync(
        [Description("planId returned by plan_update_chart")] string planId,
        [Description("The chart's GUID (from plan_update_chart)")] string chartId,
        [Description("Logical name of the table the chart belongs to")] string tableLogicalName,
        [Description("Display name of the chart")] string chartName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteUpdateChart executing: planId={PlanId}, chartId={ChartId}, name={Name}",
            planId, chartId, chartName);

        if (!Guid.TryParse(chartId, out var parsedChartId) || parsedChartId == Guid.Empty)
            return $"ERROR: chartId '{chartId}' must be a valid GUID.";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_chart_invalid_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_update_chart. Fix the spec and re-plan.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"Chart '{chartName}' was already updated.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_update_chart first.";
        }

        try
        {
            await dataverseClient.UpdateChartAsync(parsedChartId, webApiPayloadJson, cancellationToken);
            state.ExecutedPlans[planId] = webApiPayloadJson;
            state.PendingPlans.Remove(planId);

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "ExecuteUpdateChart",
                Parameters = new { chartId = parsedChartId, tableLogicalName, chartName },
                Result = new { chartId = parsedChartId },
                IsReversible = false,  // can't revert an update without the prior payload
                ReverseOp = null
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteUpdateChart",
                Detail = $"Updated chart '{chartName}' on table '{tableLogicalName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteUpdateChart"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Chart '{chartName}' (id {parsedChartId}) on table '{tableLogicalName}' updated.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteUpdateChart failed: chartId={ChartId}", parsedChartId);
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteUpdateChart",
                Detail = $"Failed to update chart '{chartName}' (id {parsedChartId}): {ex.Message}",
                Success = false
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteUpdateChart"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to update chart '{chartName}' (id {parsedChartId}): {ex.Message}.";
        }
    }
}
