using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Verifies a created table against the EXACT payload that was sent to Dataverse.
/// Loads the plan by planId from ProjectState.ExecutedPlans, so verification can
/// never disagree with what was actually planned (no hallucinated expected values).
/// </summary>
public class VerifyTableAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a created table against the exact plan that was executed. "
        + "Pass the planId from the most recent ExecuteCreateTable result — the tool loads the "
        + "plan's expected values from server state and compares them against the actual table. "
        + "ALWAYS prefer this over verify_table_creation when you have a planId.")]
    public async Task<VerificationResult> VerifyTableAgainstPlanAsync(
        [Description("planId of the table that was executed (from execute_create_table)")] string planId,
        CancellationToken cancellationToken)
    {
        // Defensive: reject planIds that clearly belong to a different verb.
        // Point the agent at the right verifier so it doesn't loop on the wrong tool.
        if (!planId.StartsWith("create_table_", StringComparison.OrdinalIgnoreCase))
        {
            string hint;
            if (planId.StartsWith("create_global_option_set_", StringComparison.OrdinalIgnoreCase))
                hint = "Use verify_global_option_set_against_plan instead.";
            else if (planId.StartsWith("create_field_", StringComparison.OrdinalIgnoreCase)
                  || planId.StartsWith("modify_field_", StringComparison.OrdinalIgnoreCase)
                  || planId.StartsWith("create_customer_lookup_", StringComparison.OrdinalIgnoreCase)
                  || planId.StartsWith("create_polymorphic_lookup_", StringComparison.OrdinalIgnoreCase))
                hint = "Use verify_field_against_plan instead.";
            else
                hint = "Pass the planId from a recent execute_create_table call.";
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("planId", planId,
                    $"WRONG PLAN TYPE — verify_table_against_plan only accepts create_table_* planIds. {hint}")],
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
        var schemaName = plan.GetProperty("SchemaName").GetString() ?? string.Empty;
        var tableLogicalName = schemaName.ToLowerInvariant();

        // Dataverse takes a moment to make table metadata queryable after POST.
        // Retry on transient failures so we don't false-FAIL right after creation.
        TableMetadata? actual = null;
        Exception? lastError = null;
        int[] delaysMs = [0, 1000, 2000];
        foreach (var delayMs in delaysMs)
        {
            if (delayMs > 0) await Task.Delay(delayMs, cancellationToken);
            try
            {
                actual = await dataverseClient.GetTableMetadataAsync(tableLogicalName, cancellationToken);
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        if (actual is null)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("table", tableLogicalName, $"NOT FOUND after retries: {lastError?.Message}")],
                Verdict: "FAIL");
        }

        var discrepancies = new List<Discrepancy>();

        // Compare every field that was in the plan against what Dataverse actually has.
        if (plan.TryGetProperty("DisplayName", out var dn) &&
            dn.TryGetProperty("LocalizedLabels", out var labels) &&
            labels.GetArrayLength() > 0 &&
            labels[0].TryGetProperty("Label", out var lbl))
        {
            Compare(discrepancies, "DisplayName", lbl.GetString() ?? "", actual.DisplayName);
        }
        if (plan.TryGetProperty("OwnershipType", out var ot))
            Compare(discrepancies, "OwnershipType", ot.GetString() ?? "", actual.OwnershipType);
        if (plan.TryGetProperty("HasNotes", out var hn))
            Compare(discrepancies, "HasNotes", hn.GetBoolean().ToString(), actual.HasNotes.ToString());
        if (plan.TryGetProperty("HasActivities", out var ha))
            Compare(discrepancies, "HasActivities", ha.GetBoolean().ToString(), actual.HasActivities.ToString());
        if (plan.TryGetProperty("ChangeTrackingEnabled", out var ct))
            Compare(discrepancies, "ChangeTrackingEnabled", ct.GetBoolean().ToString(), actual.ChangeTrackingEnabled.ToString());
        if (plan.TryGetProperty("IsAuditEnabled", out var ia) && ia.TryGetProperty("Value", out var iaVal))
            Compare(discrepancies, "IsAuditEnabled", iaVal.GetBoolean().ToString(), actual.IsAuditEnabled.ToString());

        var verdict = discrepancies.Count == 0 ? "PASS" : "FAIL";
        return new VerificationResult(
            Exists: true,
            Matches: discrepancies.Count == 0,
            Discrepancies: discrepancies,
            Verdict: verdict);
    }

    private static void Compare(List<Discrepancy> discrepancies, string property, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            discrepancies.Add(new Discrepancy(property, expected, actual));
    }
}
