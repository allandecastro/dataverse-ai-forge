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
/// Delete a Dataverse chart. Direct call (no plan/execute split — same shape as
/// DeleteFieldTool / DeleteAlternateKeyTool). Approval should be requested by
/// the agent BEFORE invoking this; the tool itself doesn't gate.
/// </summary>
public class DeleteChartTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DeleteChartTool> logger)
{
    [Description("Delete a Dataverse chart by its GUID. DESTRUCTIVE — request approval first via "
        + "request_approval. Use get_charts_list to find the chartId. After deletion the chart is "
        + "removed from every dashboard and view that referenced it.")]
    public async Task<string> DeleteChartAsync(
        [Description("The chart's GUID (savedqueryvisualizationid). Get it from get_charts_list.")] string chartId,
        [Description("Logical name of the table the chart was attached to (for audit + state tracking)")]
        string tableLogicalName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        if (!Guid.TryParse(chartId, out var parsedChartId) || parsedChartId == Guid.Empty)
            return $"ERROR: chartId '{chartId}' must be a valid GUID.";

        logger.LogInformation("DeleteChart executing: chartId={ChartId}, table={Table}", parsedChartId, tableLogicalName);

        try
        {
            await dataverseClient.DeleteChartAsync(parsedChartId, cancellationToken);

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.Charts.RemoveAll(c => c.ChartId == parsedChartId);
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    ToolName = "DeleteChart",
                    Parameters = new { chartId = parsedChartId, tableLogicalName },
                    Result = new { chartId = parsedChartId },
                    IsReversible = false,  // chart XML isn't recoverable post-delete without an export
                    ReverseOp = null
                });
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "DeleteChart",
                    Detail = $"Deleted chart {parsedChartId} from table '{tableLogicalName}'",
                    Success = true
                });
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "DeleteChart"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Chart {parsedChartId} on table '{tableLogicalName}' deleted. Any dashboards or views referencing it will fall back to defaults.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "DeleteChart failed: chartId={ChartId}", parsedChartId);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "DeleteChart",
                    Detail = $"Failed to delete chart {parsedChartId}: {ex.Message}",
                    Success = false
                });
            }, cancellationToken);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "DeleteChart"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Could not delete chart {parsedChartId}: {ex.Message}. "
                + "Common causes: chart is part of a managed solution, or the chart no longer exists.";
        }
    }
}
