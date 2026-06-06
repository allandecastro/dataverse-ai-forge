using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plan an update to an existing chart. Same DataDescription /
/// PresentationDescription template machinery as create — only the HTTP verb
/// differs (PATCH vs POST). Calls get_charts_list first if you need to find
/// the chartId.
/// </summary>
public class PlanUpdateChartTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Generate a plan for UPDATING an existing Dataverse chart (savedqueryvisualization). "
        + "Returns a planId, the new DataDescription + PresentationDescription previews, warnings, "
        + "and a risk level. Pass the planId verbatim to execute_update_chart after approval. "
        + "Use get_charts_list to find the chartId of the chart to update. Same chart-type / "
        + "aggregate / dateGrouping enums as plan_create_chart.")]
    public async Task<ChartUpdatePlan> PlanUpdateChartAsync(
        [Description("The GUID of the existing chart to update (from get_charts_list)")] string chartId,
        [Description("Logical name of the table the chart belongs to (e.g., 'opportunity')")] string tableLogicalName,
        [Description("New display name for the chart. Pass the existing name if not renaming.")] string chartName,
        [Description("Description for the chart (free text)")] string description,
        [Description("Chart type: column | bar | line | pie | funnel | doughnut | area | multiseries | comparison | comparison100")]
        string chartType,
        [Description("Logical name of the primary groupby attribute (X axis / category)")] string categoryField,
        [Description("Logical name of the aggregate attribute (Y axis / measure)")] string measureField,
        [Description("Aggregate function: sum | count | countcolumn | avg | min | max")] string measureAggregate,
        [Description("Optional second groupby attribute (comparison charts only). Empty otherwise.")]
        string secondCategoryField,
        [Description("Optional second measure (multiseries only). Empty otherwise.")] string secondMeasureField,
        [Description("Aggregate for the second measure. Empty unless secondMeasureField is set.")] string secondMeasureAggregate,
        [Description("Optional date grouping for date/datetime category fields. Empty otherwise.")] string dateGrouping,
        [Description("Optional fetch count for top-N charts. 0 means no limit.")] int topN,
        [Description("Optional chart title. Empty = no title.")] string chartTitle,
        [Description("Optional X axis title. Empty = default.")] string xAxisTitle,
        [Description("Optional Y axis title. Empty = default.")] string yAxisTitle,
        [Description("Optional legend label for measure 1 (multiseries).")] string seriesLabel1,
        [Description("Optional legend label for measure 2 (multiseries).")] string seriesLabel2,
        [Description("Optional custom palette (semicolon-separated RGB triples).")] string paletteCustomColors,
        CancellationToken cancellationToken,
        [Description("Optional FetchXML <filter> block (single <filter type=\"and|or\">…</filter>, no xmlns). "
            + "Same syntax + worked examples as plan_create_chart.")] string filterFetchXml = "",
        [Description("Optional X-axis label format (e.g. 'MMM yy', 'C0', 'P0'). Empty = default.")]
        string xAxisLabelFormat = "",
        [Description("Optional Y-axis label format. Empty = default.")] string yAxisLabelFormat = "",
        [Description("Optional value-label format. Only takes effect on chart types that show value "
            + "labels by default (column / line / pie / funnel / doughnut). Empty = no formatting.")]
        string valueLabelFormat = "",
        [Description("Optional conditional color rules — JSON array {when,color,operator?}. "
            + "Same syntax + worked examples as plan_create_chart.")] string colorRulesJson = "",
        [Description("Optional sugar — local-optionset column logical name. Same behavior as "
            + "plan_create_chart's colorByOptionSet.")] string colorByOptionSet = "")
    {
        if (!Guid.TryParse(chartId, out var parsedChartId) || parsedChartId == Guid.Empty)
        {
            return new ChartUpdatePlan(
                PlanId: $"rejected_chart_invalid_{Guid.NewGuid():N}",
                ChartId: Guid.Empty,
                TableLogicalName: tableLogicalName,
                ChartName: chartName,
                ChartType: chartType,
                Description: description ?? string.Empty,
                DataDescriptionXml: string.Empty,
                PresentationDescriptionXml: string.Empty,
                WebApiPayloadJson: string.Empty,
                Warnings: new List<string> { "REJECTED: chartId must be a valid GUID from get_charts_list." }.AsReadOnly(),
                RiskLevel: PlanRisk.Red);
        }

        // Build the plan via the same pipeline plan_create_chart uses, then
        // strip the create-only fields (primaryentitytypecode, type, isdefault)
        // — Dataverse rejects PATCHing those once a chart exists.
        var creationPlan = await new PlanCreateChartTool(dataverseClient, projectStateStore, sessionContext)
            .PlanCreateChartAsync(
                tableLogicalName, chartName, description, chartType,
                categoryField, measureField, measureAggregate,
                secondCategoryField, secondMeasureField, secondMeasureAggregate,
                dateGrouping, topN, chartTitle, xAxisTitle, yAxisTitle,
                seriesLabel1, seriesLabel2, paletteCustomColors,
                cancellationToken,
                filterFetchXml,
                xAxisLabelFormat, yAxisLabelFormat, valueLabelFormat,
                colorRulesJson, colorByOptionSet);

        if (creationPlan.PlanId.StartsWith("rejected_chart_invalid_", StringComparison.Ordinal))
        {
            // Forward the rejection — same warnings, just retyped as an UpdatePlan.
            return new ChartUpdatePlan(
                PlanId: creationPlan.PlanId,
                ChartId: parsedChartId,
                TableLogicalName: creationPlan.TableLogicalName,
                ChartName: creationPlan.ChartName,
                ChartType: creationPlan.ChartType,
                Description: creationPlan.Description,
                DataDescriptionXml: string.Empty,
                PresentationDescriptionXml: string.Empty,
                WebApiPayloadJson: string.Empty,
                Warnings: creationPlan.Warnings,
                RiskLevel: creationPlan.RiskLevel);
        }

        // PATCH payload: drop primaryentitytypecode + type + isdefault.
        var patchPayload = new Dictionary<string, object>
        {
            ["name"] = chartName,
            ["description"] = description ?? string.Empty,
            ["datadescription"] = creationPlan.DataDescriptionXml,
            ["presentationdescription"] = creationPlan.PresentationDescriptionXml
        };
        var patchPayloadJson = JsonSerializer.Serialize(patchPayload, new JsonSerializerOptions { WriteIndented = true });

        // Replace the create plan we just stored with the update payload, and
        // re-key under an `update_chart_*` planId so the executor knows which
        // verb to use.
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.PendingPlans.Remove(creationPlan.PlanId);
        var planId = $"update_chart_{Guid.NewGuid():N}";
        state.AddPendingPlan(planId, patchPayloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new ChartUpdatePlan(
            PlanId: planId,
            ChartId: parsedChartId,
            TableLogicalName: creationPlan.TableLogicalName,
            ChartName: chartName,
            ChartType: creationPlan.ChartType,
            Description: description ?? string.Empty,
            DataDescriptionXml: creationPlan.DataDescriptionXml,
            PresentationDescriptionXml: creationPlan.PresentationDescriptionXml,
            WebApiPayloadJson: patchPayloadJson,
            Warnings: creationPlan.Warnings,
            RiskLevel: creationPlan.RiskLevel);
    }
}
