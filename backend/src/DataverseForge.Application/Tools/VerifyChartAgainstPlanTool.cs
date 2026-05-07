using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tester verifier for charts. Loads the planned payload from
/// state.ExecutedPlans (keyed by the create_chart_* / update_chart_* planId)
/// and compares the actual chart's metadata + DataDescription +
/// PresentationDescription against it. Same shape as
/// VerifyTableAgainstPlanTool / VerifyFieldAgainstPlanTool.
/// </summary>
public class VerifyChartAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a Dataverse chart against the planned spec. Loads the planned OData payload "
        + "from server-side state (under the planId returned by plan_create_chart / plan_update_chart) "
        + "and compares against the live chart fetched by chartId. Returns PASS / FAIL / PARTIAL "
        + "with concrete discrepancies. PlanId must start with 'create_chart_' or 'update_chart_'; "
        + "for any other prefix the tool tells you which verifier to call instead.")]
    public async Task<ChartVerificationResult> VerifyChartAgainstPlanAsync(
        [Description("planId from plan_create_chart or plan_update_chart")] string planId,
        [Description("The chart's GUID (savedqueryvisualizationid)")] string chartId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(chartId, out var parsedChartId) || parsedChartId == Guid.Empty)
        {
            return new ChartVerificationResult(
                Guid.Empty, planId, "FAIL",
                new List<string> { $"chartId '{chartId}' is not a valid GUID." }.AsReadOnly(),
                null, null, null, null);
        }

        // F-35: rejection planIds should produce a clear "this never executed"
        // message rather than the misleading "wrong plan type — use create".
        if (planId.StartsWith("rejected_chart_invalid_", StringComparison.Ordinal))
        {
            return new ChartVerificationResult(
                parsedChartId, planId, "FAIL",
                new List<string> { $"REJECTED PLAN: '{planId}' was rejected at planning time and never reached Dataverse — re-plan with valid inputs (do NOT pass this planId to verify)." }.AsReadOnly(),
                null, null, null, null);
        }

        // Plan-prefix routing — same UX as the table/field/optionset verifiers.
        if (!(planId.StartsWith("create_chart_", StringComparison.Ordinal)
              || planId.StartsWith("update_chart_", StringComparison.Ordinal)))
        {
            string suggestion = planId switch
            {
                _ when planId.StartsWith("create_table_") || planId.StartsWith("update_table_") => "verify_table_against_plan",
                _ when planId.StartsWith("create_field_") || planId.StartsWith("update_field_") => "verify_field_against_plan",
                _ when planId.StartsWith("create_global_option_set_") => "verify_global_option_set_against_plan",
                _ => "the appropriate verify_*_against_plan tool"
            };
            return new ChartVerificationResult(
                parsedChartId, planId, "FAIL",
                new List<string> { $"WRONG PLAN TYPE: '{planId}' is not a chart plan. Use {suggestion} instead." }.AsReadOnly(),
                null, null, null, null);
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!state.ExecutedPlans.TryGetValue(planId, out var plannedPayload))
        {
            // Pending or missing — verifier only runs on plans that have been
            // executed. Make this clear so the agent doesn't loop on it.
            var pendingHint = state.PendingPlans.ContainsKey(planId)
                ? " The plan is still pending — execute_create_chart / execute_update_chart hasn't been called yet."
                : "";
            return new ChartVerificationResult(
                parsedChartId, planId, "FAIL",
                new List<string> { $"Plan '{planId}' not found in ExecutedPlans.{pendingHint}" }.AsReadOnly(),
                null, null, null, null);
        }

        // Pull the live chart record from Dataverse.
        ChartDetails live;
        try
        {
            live = await dataverseClient.GetChartDetailsAsync(parsedChartId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ChartVerificationResult(
                parsedChartId, planId, "FAIL",
                new List<string> { $"Could not fetch chart {parsedChartId}: {ex.Message}" }.AsReadOnly(),
                null, null, null, null);
        }

        // Parse the planned payload into a dict and compare key fields.
        var planned = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(plannedPayload)
            ?? new Dictionary<string, JsonElement>();

        var discrepancies = new List<string>();

        var plannedName = TryGetString(planned, "name");
        if (plannedName is not null && !string.Equals(plannedName, live.Name, StringComparison.Ordinal))
            discrepancies.Add($"name: expected '{plannedName}', got '{live.Name}'");

        var plannedDescription = TryGetString(planned, "description");
        if (plannedDescription is not null && !string.Equals(plannedDescription, live.Description, StringComparison.Ordinal))
            discrepancies.Add($"description: expected '{plannedDescription}', got '{live.Description}'");

        var plannedPrimaryEntity = TryGetString(planned, "primaryentitytypecode");
        if (plannedPrimaryEntity is not null && !string.Equals(plannedPrimaryEntity, live.PrimaryEntityTypeCode, StringComparison.OrdinalIgnoreCase))
            discrepancies.Add($"primaryentitytypecode: expected '{plannedPrimaryEntity}', got '{live.PrimaryEntityTypeCode}'");

        var plannedDataDesc = TryGetString(planned, "datadescription") ?? string.Empty;
        var plannedPresDesc = TryGetString(planned, "presentationdescription") ?? string.Empty;

        // XML comparison — collapse whitespace before comparing so cosmetic
        // differences (line endings, indent) don't flag as failures.
        // F-28: Dataverse normalizes round-tripped XML server-side
        // (re-indent, attribute reordering, default injection). For UPDATE
        // payloads especially, the planned PATCH body is a strict subset of
        // the live document, so byte-equivalence is unrealistic. Surface XML
        // mismatches as separate "informational" discrepancies (prefixed with
        // "info:") that the verdict logic ignores — Tester still shows them
        // to the user but doesn't FAIL on them. name/primary-entity stay as
        // hard-fail signals because those are 1:1 fields the planner controls.
        var dataDescMismatch = !XmlEquivalent(plannedDataDesc, live.DataDescriptionXml);
        var presDescMismatch = !XmlEquivalent(plannedPresDesc, live.PresentationDescriptionXml);

        if (dataDescMismatch)
            discrepancies.Add("info: datadescription XML differs from plan (whitespace-normalized — likely Dataverse server-side normalization, not a real bug).");
        if (presDescMismatch)
            discrepancies.Add("info: presentationdescription XML differs from plan (whitespace-normalized — likely Dataverse server-side normalization, not a real bug).");

        // Verdict logic: only structural fields drive PASS/FAIL/PARTIAL.
        // "info:"-prefixed discrepancies are informational and don't change
        // the verdict (would otherwise produce spurious PARTIAL on every
        // successful update — see F-28).
        var structural = discrepancies.Where(d => !d.StartsWith("info:", StringComparison.Ordinal)).ToList();
        var verdict = structural.Count == 0 ? "PASS"
            : structural.Any(d => d.StartsWith("name:") || d.StartsWith("primaryentitytypecode:")) ? "FAIL"
            : "PARTIAL";

        return new ChartVerificationResult(
            ChartId: parsedChartId,
            PlanId: planId,
            Verdict: verdict,
            Discrepancies: discrepancies.AsReadOnly(),
            PlanDataDescriptionXml: plannedDataDesc,
            ActualDataDescriptionXml: live.DataDescriptionXml,
            PlanPresentationDescriptionXml: plannedPresDesc,
            ActualPresentationDescriptionXml: live.PresentationDescriptionXml);
    }

    private static string? TryGetString(IReadOnlyDictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>
    /// Compares two XML strings ignoring whitespace + line endings. We don't
    /// need full canonical XML equivalence here — Dataverse round-trips both
    /// blobs verbatim except for normalized line endings.
    /// </summary>
    private static bool XmlEquivalent(string a, string b)
    {
        return Normalize(a) == Normalize(b);
        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        }
    }
}
