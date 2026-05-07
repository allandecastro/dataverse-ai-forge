using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateChartTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateChartTool> logger)
{
    [Description("Execute the creation of a Dataverse chart (savedqueryvisualization). Pass the planId "
        + "from plan_create_chart — the OData JSON payload (with embedded DataDescription + "
        + "PresentationDescription XML) was stored server-side and will be loaded automatically. "
        + "Do NOT try to construct or pass the payload yourself.")]
    public async Task<string> ExecuteCreateChartAsync(
        [Description("planId returned by plan_create_chart — must match exactly. Payload is stored "
            + "server-side; do NOT construct the OData JSON yourself.")] string planId,
        [Description("Logical name of the table the chart attaches to")] string tableLogicalName,
        [Description("Display name of the chart")] string chartName,
        [Description("Chart type (column / bar / line / pie / funnel / doughnut / area / multiseries / comparison / comparison100)")]
        string chartType,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateChart executing: planId={PlanId}, table={Table}, chart={Chart}, type={Type}",
            planId, tableLogicalName, chartName, chartType);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_chart_invalid_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_chart. Fix the spec and re-plan; do NOT execute this planId.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteCreateChart: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"Chart '{chartName}' is already on '{tableLogicalName}'. DO NOT retry, DO NOT re-plan.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteCreateChart: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_chart first and pass its returned planId verbatim.";
        }

        var solutionUniqueName = state.SolutionUniqueName; // capture before await

        try
        {
            var chartId = await dataverseClient.CreateChartAsync(webApiPayloadJson, cancellationToken);

            // Add the new chart to the active solution. Charts aren't auto-included
            // when a table is in the solution with doNotIncludeSubcomponents=true.
            var solutionAddNote = string.Empty;
            if (!string.IsNullOrWhiteSpace(solutionUniqueName))
            {
                try
                {
                    await dataverseClient.AddSolutionComponentAsync(chartId, SolutionComponentType.SavedQueryVisualization, solutionUniqueName, doNotIncludeSubcomponents: true, cancellationToken);
                    solutionAddNote = $" Added to solution '{solutionUniqueName}'.";
                }
                catch (Exception solEx)
                {
                    logger.LogWarning(solEx, "AddSolutionComponent failed for chart {ChartName} ({ChartId}) on solution {Solution}",
                        chartName, chartId, solutionUniqueName);
                    solutionAddNote = $" WARNING: chart created in env but could NOT be added to solution '{solutionUniqueName}': {solEx.Message}.";
                }
            }
            else
            {
                solutionAddNote = " WARNING: no active solution selected — the chart is in the default solution.";
            }

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.Charts.Add(new CreatedChart(chartId, tableLogicalName, chartName, chartType, DateTime.UtcNow));
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    ToolName = "ExecuteCreateChart",
                    Parameters = new { tableLogicalName, chartName, chartType },
                    Result = new { chartId },
                    IsReversible = true,
                    ReverseOp = new ReverseOperation
                    {
                        ToolName = "DeleteChart",
                        Parameters = new { chartId, tableLogicalName },
                        Warning = "Deleting a chart removes it from every dashboard and view that references it.",
                        RiskLevel = RiskLevel.Medium
                    }
                });
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "ExecuteCreateChart",
                    Detail = $"Created chart '{chartName}' ({chartType}) on table '{tableLogicalName}'",
                    Success = true
                });
            }, cancellationToken);

            logger.LogInformation("ExecuteCreateChart succeeded: chartId={ChartId}, name={Name}", chartId, chartName);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateChart"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Chart '{chartName}' ({chartType}) created on table '{tableLogicalName}' with ChartId={chartId}.{solutionAddNote}";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateChart failed: table={Table}, name={Name}", tableLogicalName, chartName);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "ExecuteCreateChart",
                    Detail = $"Failed to create chart '{chartName}' on table '{tableLogicalName}': {ex.Message}",
                    Success = false
                });
            }, cancellationToken);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateChart"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create chart '{chartName}' ({chartType}) on table '{tableLogicalName}': {ex.Message}. "
                + "Check the data description fields exist and the user has System Customizer privilege.";
        }
    }
}
