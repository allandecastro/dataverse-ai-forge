using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plan creation of a Dataverse chart (savedqueryvisualization). Validates
/// field existence + chart-type / aggregate enums, builds the
/// DataDescription FetchXML, picks the matching PresentationDescription
/// template, and stores the full OData payload server-side under a planId
/// so the agent can't mangle it on the way to ExecuteCreateChart.
/// </summary>
public class PlanCreateChartTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Generate a plan for creating a Dataverse chart (savedqueryvisualization). "
        + "Returns a planId, the DataDescription + PresentationDescription previews, warnings, and a "
        + "risk level. The full OData JSON payload is stored server-side under planId so it cannot "
        + "be mangled. Pass the planId verbatim to execute_create_chart after approval. "
        + "Org-owned charts only (savedqueryvisualization). "
        + "Supported chartType values: column, bar, line, pie, funnel, doughnut, area, multiseries, "
        + "comparison, comparison100. "
        + "Comparison charts need secondCategoryField. multiseries needs secondMeasureField + "
        + "secondMeasureAggregate. Pie/funnel/doughnut don't support multi-series.")]
    public async Task<ChartCreationPlan> PlanCreateChartAsync(
        [Description("The logical name of the table the chart attaches to (e.g., 'opportunity', 'eppc_gift')")]
        string tableLogicalName,
        [Description("The chart's display name (e.g., 'Top 10 Customers')")] string chartName,
        [Description("Description for the chart (free text)")] string description,
        [Description("Chart type: column | bar | line | pie | funnel | doughnut | area | multiseries | comparison | comparison100")]
        string chartType,
        [Description("Logical name of the primary groupby attribute (the X axis / category). E.g. 'industrycode'")]
        string categoryField,
        [Description("Logical name of the aggregate attribute (the Y axis / measure). E.g. 'estimatedvalue'")]
        string measureField,
        [Description("Aggregate function: sum | count | countcolumn | avg | min | max")]
        string measureAggregate,
        [Description("Optional second groupby attribute (for comparison charts only — the stack/sub-category dimension). Empty otherwise.")]
        string secondCategoryField,
        [Description("Optional second measure attribute (for multiseries charts only — e.g. 'actualvalue' alongside 'estimatedvalue'). Empty otherwise.")]
        string secondMeasureField,
        [Description("Aggregate for the second measure (sum/count/avg/min/max). Empty unless secondMeasureField is set.")]
        string secondMeasureAggregate,
        [Description("Optional date grouping for date/datetime category fields: day | week | month | quarter | year | fiscalperiod | fiscalyear. Empty otherwise.")]
        string dateGrouping,
        [Description("Optional fetch count for top-N charts (e.g. 10 for 'Top 10'). 0 or empty means no limit.")]
        int topN,
        [Description("Optional chart title shown above the plot area. Empty means no title.")] string chartTitle,
        [Description("Optional X axis title. Empty means default.")] string xAxisTitle,
        [Description("Optional Y axis title. Empty means default.")] string yAxisTitle,
        [Description("Optional legend label for measure 1 (multiseries only, e.g. 'Estimated'). Empty falls back to 'Series 1'.")]
        string seriesLabel1,
        [Description("Optional legend label for measure 2 (multiseries only, e.g. 'Actual'). Empty falls back to 'Series 2'.")]
        string seriesLabel2,
        [Description("Optional custom color palette as semicolon-separated RGB triples like '91,151,213; 237,125,49'. Empty uses Dataverse defaults.")]
        string paletteCustomColors,
        CancellationToken cancellationToken,
        [Description("Optional FetchXML <filter> block — restricts which rows the chart aggregates. "
            + "MUST be a single <filter type=\"and|or\">…</filter> element with no xmlns. Use Dataverse "
            + "operators verbatim (eq-userid, this-fiscal-year, last-x-months, on-or-after, in, etc.). "
            + "Worked examples: "
            + "(a) my open rows: <filter type=\"and\"><condition attribute=\"ownerid\" operator=\"eq-userid\"/><condition attribute=\"statecode\" operator=\"eq\" value=\"0\"/></filter> "
            + "(b) this fiscal year: <filter type=\"and\"><condition attribute=\"createdon\" operator=\"this-fiscal-year\"/></filter>. "
            + "Empty = no filter.")]
        string filterFetchXml = "",
        [Description("Optional X-axis label format (.NET format string). Examples: 'MMM yy' for "
            + "'Apr 2026', 'C0' for currency without decimals, 'P0' for percent, '#,###' for grouped. "
            + "Empty = Dataverse default. No effect on pie/funnel/doughnut (they have no axes).")]
        string xAxisLabelFormat = "",
        [Description("Optional Y-axis label format (same syntax as xAxisLabelFormat). "
            + "Empty = Dataverse default.")]
        string yAxisLabelFormat = "",
        [Description("Optional data-label format on the chart's values. Examples: 'P0' for "
            + "percentages, 'C0' for currency, '#,###' for grouped numbers. Only takes effect on "
            + "chart types that ship with value labels enabled — column, line, pie, funnel, doughnut. "
            + "Bar/area/multiseries/comparison/comparison100 don't render value labels by default; "
            + "the format is emitted but ignored. Empty = no formatting applied.")]
        string valueLabelFormat = "",
        [Description("Optional conditional color rules — JSON array of "
            + "{ \"when\": \"<value>\", \"color\": \"#RRGGBB\" or \"R,G,B\", "
            + "\"operator\": \"eq|ne|gt|lt|gte|lte\" (default eq) }. "
            + "Each rule colors the data points that match. Best for category charts where each "
            + "value should map to a fixed color (e.g. priority Red/Yellow/Green, status Active/Inactive). "
            + "Worked example for Pie by Priority: "
            + "[{\"when\":\"1\",\"color\":\"#D62728\"},{\"when\":\"2\",\"color\":\"#FFC000\"},{\"when\":\"3\",\"color\":\"#2CA02C\"}]. "
            + "Effective on column, bar, pie, funnel, doughnut, comparison100. Empty = use default palette.")]
        string colorRulesJson = "",
        [Description("Optional sugar: a local-optionset column logical name (e.g. 'prioritycode'). "
            + "When set, the planner fetches the column's option metadata and emits one ColorRule "
            + "per option using its maker-portal Color. Skips options without an assigned color. "
            + "Combines with colorRulesJson: explicit rules take precedence over auto-generated ones "
            + "for the same value. Works alongside paletteCustomColors. Empty = no auto-coloring. "
            + "Tip: this is what the Modern UI's 'Use option-set colors' toggle does in the chart "
            + "designer.")]
        string colorByOptionSet = "")
    {
        var warnings = new List<string>();

        // ─── Input normalization + early structural checks ────────────
        var t = (tableLogicalName ?? "").ToLowerInvariant();
        var ct = (chartType ?? "").ToLowerInvariant();
        var category = (categoryField ?? "").ToLowerInvariant();
        var measure = (measureField ?? "").ToLowerInvariant();
        var aggregate = (measureAggregate ?? "").ToLowerInvariant();
        var secondCat = (secondCategoryField ?? "").ToLowerInvariant();
        var secondMeasure = (secondMeasureField ?? "").ToLowerInvariant();
        var secondAggregate = (secondMeasureAggregate ?? "").ToLowerInvariant();
        var dateGrp = (dateGrouping ?? "").ToLowerInvariant();

        if (!ChartTemplates.IsSupported(ct))
        {
            return Reject(t, chartName, ct, description,
                $"Unsupported chartType '{chartType}'. Supported: {string.Join(", ", ChartTemplates.SupportedChartTypes)}.");
        }

        if (!ChartDataDescriptionBuilder.SupportedAggregates.Contains(aggregate))
        {
            return Reject(t, chartName, ct, description,
                $"Unsupported measureAggregate '{measureAggregate}'. Supported: {string.Join(", ", ChartDataDescriptionBuilder.SupportedAggregates)}.");
        }

        if (!string.IsNullOrEmpty(dateGrp) && !ChartDataDescriptionBuilder.SupportedDateGroupings.Contains(dateGrp))
        {
            warnings.Add($"Unrecognized dateGrouping '{dateGrouping}' — emitting anyway, but Dataverse may reject. Supported: {string.Join(", ", ChartDataDescriptionBuilder.SupportedDateGroupings)}.");
        }

        if (string.IsNullOrWhiteSpace(category)) warnings.Add("categoryField is empty — chart will have no X-axis grouping.");
        if (string.IsNullOrWhiteSpace(measure)) warnings.Add("measureField is empty — chart will have no Y-axis aggregate.");

        // Multi-series + comparison shape checks.
        var isMulti = ChartTemplates.IsMultiSeries(ct);
        var isComparison = ChartTemplates.IsComparison(ct);

        if (isMulti)
        {
            if (string.IsNullOrWhiteSpace(secondMeasure))
                return Reject(t, chartName, ct, description, "multiseries chart requires secondMeasureField.");
            if (string.IsNullOrWhiteSpace(secondAggregate))
                return Reject(t, chartName, ct, description, "multiseries chart requires secondMeasureAggregate.");
            if (!ChartDataDescriptionBuilder.SupportedAggregates.Contains(secondAggregate))
                return Reject(t, chartName, ct, description,
                    $"Unsupported secondMeasureAggregate '{secondMeasureAggregate}'. Supported: {string.Join(", ", ChartDataDescriptionBuilder.SupportedAggregates)}.");
        }
        else if (!string.IsNullOrWhiteSpace(secondMeasure))
        {
            warnings.Add($"secondMeasureField '{secondMeasureField}' provided but chartType '{ct}' is single-measure — ignored.");
        }

        if (isComparison)
        {
            if (string.IsNullOrWhiteSpace(secondCat))
                return Reject(t, chartName, ct, description, "comparison / comparison100 chart requires secondCategoryField (the stack/sub-category dimension).");
            // Dataverse rejects two groupbys on the same attribute with HTTP 400
            // 0x8004e01b "Same attribute: X, alias: groupby_secondary cannot be
            // used as a Group By more than once". Real session 2026-05-05: agent
            // tried (statuscode, statuscode) for a 100% stacked chart with one
            // category. The fix is to pick a DIFFERENT field for the stack.
            if (string.Equals(category, secondCat, StringComparison.Ordinal))
            {
                return Reject(t, chartName, ct, description,
                    $"comparison / comparison100 chart needs TWO different groupby fields, but categoryField "
                    + $"and secondCategoryField are both '{category}'. Pick a second dimension that varies "
                    + $"within each '{category}' value (e.g. category=eppc_type, secondCategory=statuscode "
                    + $"shows status mix per gift type). If the user only has one dimension, use a "
                    + $"single-series chart (column / bar / pie) instead of comparison*.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(secondCat))
        {
            warnings.Add($"secondCategoryField '{secondCategoryField}' provided but chartType '{ct}' is single-category — ignored.");
        }

        // pie/funnel/doughnut — single measure, single category. Reject early.
        if (ct is "pie" or "funnel" or "doughnut")
        {
            if (!string.IsNullOrWhiteSpace(secondMeasure) || !string.IsNullOrWhiteSpace(secondCat))
            {
                return Reject(t, chartName, ct, description,
                    $"{ct} charts only support a single measure and single category — drop secondMeasureField / secondCategoryField.");
            }
        }

        // ─── Live schema validation ───────────────────────────────────
        TableDetails? details = null;
        try
        {
            details = await dataverseClient.GetTableDetailsAsync(t, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Reject(t, chartName, ct, description,
                $"Table '{t}' not found in this environment. Custom tables typically have a publisher prefix. Underlying lookup error: {ex.Message}");
        }

        var available = new HashSet<string>(
            details.Columns.Select(c => c.LogicalName.ToLowerInvariant()),
            StringComparer.Ordinal);

        var referenced = new[] { category, measure, secondCat, secondMeasure }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var missing = referenced.Where(f => !available.Contains(f)).ToList();
        if (missing.Count > 0)
        {
            // Detect the most common chart-build hallucination: agent prefixed a
            // standard field name with the publisher prefix (e.g. eppc_statecode,
            // adi_ownerid). Standard fields are universal and unprefixed. Surface
            // the corrected field name explicitly so the agent fixes the call
            // rather than asking the user to "add the missing field".
            var standardSuggestions = missing
                .Select(m => new { Bad = m, Fixed = TryUnprefixStandardField(m) })
                .Where(x => x.Fixed is not null && available.Contains(x.Fixed!))
                .ToList();

            string hint;
            if (standardSuggestions.Count > 0)
            {
                var suggestionList = string.Join("; ",
                    standardSuggestions.Select(s => $"'{s.Bad}' → use '{s.Fixed}' (it's a standard field, never prefixed)"));
                hint = "STANDARD-FIELD MISTAKE: " + suggestionList
                    + ". Re-call plan_create_chart with the corrected field name(s). "
                    + "Do NOT hand off to add fields — they already exist under their bare names.";
            }
            else
            {
                var sample = string.Join(", ", available.OrderBy(s => s).Take(20));
                hint = $"Chart creation will fail at Dataverse with HTTP 400. Available logical names (sample): {sample}.";
            }

            return Reject(t, chartName, ct, description,
                $"The following field(s) do NOT exist on table '{t}': {string.Join(", ", missing)}. " + hint);
        }

        // ─── Optional <filter> validation ────────────────────────────
        // Validate up-front so any rejection comes back as a plan warning
        // rather than an opaque HTTP 400 from Dataverse later. Empty is fine.
        string? validatedFilter = null;
        if (!string.IsNullOrWhiteSpace(filterFetchXml))
        {
            try
            {
                validatedFilter = ChartDataDescriptionBuilder.ValidateFilterFetchXml(filterFetchXml);
            }
            catch (ArgumentException ax)
            {
                return Reject(t, chartName, ct, description,
                    $"filterFetchXml is invalid — {ax.Message} Pass a single <filter type=\"and|or\">…</filter> element verbatim, no xmlns.");
            }
        }

        // ─── Color rules: not supported by Dataverse server-side charts ───
        // Earlier versions tried to emit <Color><Conditional><ColorRule>…</ColorRule>
        // </Conditional></Color> inside <Series>, but Dataverse parses <Color>
        // as a scalar System.Drawing.Color and rejects any nested Conditional
        // block (HTTP 400 0x8004e016, real session 2026-05-05). We still validate
        // the JSON so a malformed payload comes back as a precise rejection,
        // but we surface a plain "not supported" warning instead of injecting
        // anything into the chart XML. Same treatment for colorByOptionSet —
        // option-set chip colors don't flow into chart presentation either.
        if (!string.IsNullOrWhiteSpace(colorRulesJson))
        {
            try
            {
                ChartTemplates.ValidateAndRenderColorRules(colorRulesJson);
                warnings.Add(ChartTemplates.ColorRulesNotSupportedReason);
            }
            catch (ArgumentException ax)
            {
                return Reject(t, chartName, ct, description,
                    $"colorRulesJson is invalid — {ax.Message} Expected non-empty JSON array of {{when,color,operator}} objects.");
            }
        }

        if (!string.IsNullOrWhiteSpace(colorByOptionSet))
        {
            warnings.Add(
                $"colorByOptionSet '{colorByOptionSet.ToLowerInvariant()}' was requested but: "
                + ChartTemplates.ColorRulesNotSupportedReason);
        }

        // ─── Render the two XML blobs ────────────────────────────────
        var topNValue = topN > 0 ? (int?)topN : null;
        var dataDescriptionXml = ChartDataDescriptionBuilder.Render(
            t, ct, category, measure, aggregate,
            secondCat, secondMeasure, secondAggregate,
            dateGrp, topNValue, validatedFilter);

        var presentationDescriptionXml = ChartTemplates.Render(
            ct, chartTitle, xAxisTitle, yAxisTitle,
            seriesLabel1, seriesLabel2, paletteCustomColors,
            xAxisLabelFormat, yAxisLabelFormat, valueLabelFormat,
            colorRulesJson);

        // ─── Build the OData payload ─────────────────────────────────
        var payload = new Dictionary<string, object>
        {
            ["name"] = chartName,
            ["description"] = description ?? string.Empty,
            ["primaryentitytypecode"] = t,
            ["datadescription"] = dataDescriptionXml,
            ["presentationdescription"] = presentationDescriptionXml,
            // Type 1 = chart (not web-resource). Org-owned charts also need
            // isdefault=false unless the user explicitly wants the default.
            ["type"] = 1,
            ["isdefault"] = false
        };
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var planId = $"create_chart_{Guid.NewGuid():N}";
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        // Risk level: charts are non-destructive but visible to users —
        // yellow for the simple types, orange for comparison/multi-series.
        var riskLevel = (isMulti || isComparison) ? "orange" : "yellow";

        return new ChartCreationPlan(
            PlanId: planId,
            TableLogicalName: t,
            ChartName: chartName,
            ChartType: ct,
            Description: description ?? string.Empty,
            DataDescriptionXml: dataDescriptionXml,
            PresentationDescriptionXml: presentationDescriptionXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: riskLevel);
    }

    /// <summary>
    /// Standard Dataverse fields are universal — they exist on every table
    /// and are NEVER prefixed with the publisher prefix. The agent regularly
    /// hallucinates `eppc_statecode` / `adi_ownerid` / etc. when building a
    /// chart against a custom table. This helper recognizes that mistake and
    /// returns the corrected bare logical name; otherwise null.
    ///
    /// Returns null if the input doesn't match any known prefix-stripped
    /// standard field — i.e. when the missing field is genuinely missing.
    /// </summary>
    private static string? TryUnprefixStandardField(string fieldLogicalName)
    {
        if (string.IsNullOrEmpty(fieldLogicalName)) return null;
        // Set kept tight on purpose — the universal-attribute set on every
        // Dataverse entity is small and stable. If a user passes
        // `eppc_createdby` we know they meant `createdby`, but if they pass
        // `eppc_giftname` (which IS prefixed and IS a real custom field) we
        // must NOT rewrite it.
        var standardSuffixes = new[]
        {
            "statecode", "statuscode",
            "ownerid", "owningbusinessunit", "owninguser", "owningteam",
            "createdby", "createdon", "createdonbehalfby",
            "modifiedby", "modifiedon", "modifiedonbehalfby",
            "versionnumber", "importsequencenumber", "overriddencreatedon"
        };
        var underscoreIdx = fieldLogicalName.IndexOf('_');
        if (underscoreIdx <= 0 || underscoreIdx == fieldLogicalName.Length - 1)
            return null;
        var suffix = fieldLogicalName[(underscoreIdx + 1)..];
        return standardSuffixes.Contains(suffix, StringComparer.Ordinal) ? suffix : null;
    }

    /// <summary>
    /// Build a rejection plan with a "rejected_chart_invalid_*" planId so
    /// ExecuteCreateChartTool can detect and refuse to act on it.
    /// </summary>
    private static ChartCreationPlan Reject(string table, string chartName, string chartType, string description, string reason)
        => new(
            PlanId: $"rejected_chart_invalid_{Guid.NewGuid():N}",
            TableLogicalName: table,
            ChartName: chartName,
            ChartType: chartType,
            Description: description ?? string.Empty,
            DataDescriptionXml: string.Empty,
            PresentationDescriptionXml: string.Empty,
            WebApiPayloadJson: string.Empty,
            Warnings: new List<string> { "REJECTED: " + reason }.AsReadOnly(),
            RiskLevel: "red");
}
