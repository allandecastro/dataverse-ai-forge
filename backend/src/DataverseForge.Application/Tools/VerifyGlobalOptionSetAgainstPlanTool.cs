using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Verifies a created global option set against the EXACT payload that was
/// sent to Dataverse. Without this tool, the Tester would fall back to
/// VerifyTableAgainstPlan / VerifyFieldAgainstPlan with global-option-set
/// planIds and get rejected with WRONG-PLAN-TYPE — leading to repeated
/// "no field plan info, cannot verify" reports for newly-created option sets.
/// </summary>
public class VerifyGlobalOptionSetAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a created global option set against the exact plan that was executed. "
        + "Pass the planId from the most recent execute_create_global_option_set result — the tool "
        + "loads the planned Name + Options from project state and compares them against what "
        + "Dataverse actually has. Use this — NOT verify_table_against_plan, NOT "
        + "verify_field_against_plan — when the planId starts with 'create_global_option_set_'.")]
    public async Task<VerificationResult> VerifyGlobalOptionSetAgainstPlanAsync(
        [Description("planId of the global option set that was executed (from execute_create_global_option_set). Must start with 'create_global_option_set_'.")] string planId,
        CancellationToken cancellationToken)
    {
        if (!planId.StartsWith("create_global_option_set_", StringComparison.OrdinalIgnoreCase))
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("planId", planId,
                    "WRONG PLAN TYPE — verify_global_option_set_against_plan only accepts "
                    + "create_global_option_set_* planIds. For tables use verify_table_against_plan; "
                    + "for fields/lookups use verify_field_against_plan.")],
                Verdict: "FAIL");
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!state.ExecutedPlans.TryGetValue(planId, out var payloadJson))
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("plan", planId, "NOT FOUND in ExecutedPlans")],
                Verdict: "FAIL");
        }

        var plan = JsonDocument.Parse(payloadJson).RootElement;
        var name = plan.TryGetProperty("Name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("Name", "(plan)", "Plan payload has no Name property")],
                Verdict: "FAIL");
        }

        // Dataverse takes a moment to expose new metadata. Retry briefly so
        // we don't false-FAIL on eventual-consistency lag.
        IReadOnlyList<OptionInfo> actualOptions = Array.Empty<OptionInfo>();
        Exception? lastError = null;
        int[] delaysMs = [0, 1000, 2000];
        var found = false;
        foreach (var delayMs in delaysMs)
        {
            if (delayMs > 0) await Task.Delay(delayMs, cancellationToken);
            try
            {
                actualOptions = await dataverseClient.GetGlobalOptionSetOptionsAsync(name, cancellationToken);
                if (actualOptions.Count > 0)
                {
                    found = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        if (!found)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("optionSet", name,
                    $"NOT FOUND or returned 0 options after retries: {lastError?.Message ?? "(no options returned)"}")],
                Verdict: "FAIL");
        }

        var discrepancies = new List<Discrepancy>();

        // Compare planned vs actual option counts. Dataverse may add system
        // options on certain types (state/status), but plain Picklist global
        // option sets should have exactly the planned set.
        if (plan.TryGetProperty("Options", out var plannedOptionsEl)
            && plannedOptionsEl.ValueKind == JsonValueKind.Array)
        {
            var plannedCount = plannedOptionsEl.GetArrayLength();
            if (plannedCount != actualOptions.Count)
            {
                discrepancies.Add(new Discrepancy(
                    "OptionCount",
                    plannedCount.ToString(),
                    actualOptions.Count.ToString()));
            }

            // Verify each planned (Value, Label) is present in the actual set.
            // This catches mis-typed labels or skipped options at execution time.
            var actualByValue = actualOptions.ToDictionary(o => o.Value, o => o.Label);
            foreach (var plannedOpt in plannedOptionsEl.EnumerateArray())
            {
                if (!plannedOpt.TryGetProperty("Value", out var vEl) || !vEl.TryGetInt32(out var v)) continue;
                var plannedLabel = string.Empty;
                if (plannedOpt.TryGetProperty("Label", out var lEl)
                    && lEl.TryGetProperty("LocalizedLabels", out var llEl)
                    && llEl.GetArrayLength() > 0
                    && llEl[0].TryGetProperty("Label", out var labelStrEl))
                {
                    plannedLabel = labelStrEl.GetString() ?? string.Empty;
                }
                if (!actualByValue.TryGetValue(v, out var actualLabel))
                {
                    discrepancies.Add(new Discrepancy(
                        $"Option[{v}]",
                        plannedLabel,
                        "MISSING from actual option set"));
                }
                else if (!string.Equals(plannedLabel, actualLabel, StringComparison.Ordinal))
                {
                    discrepancies.Add(new Discrepancy(
                        $"Option[{v}].Label",
                        plannedLabel,
                        actualLabel));
                }
            }
        }

        return new VerificationResult(
            Exists: true,
            Matches: discrepancies.Count == 0,
            Discrepancies: discrepancies,
            Verdict: discrepancies.Count == 0 ? "PASS" : "FAIL");
    }
}
