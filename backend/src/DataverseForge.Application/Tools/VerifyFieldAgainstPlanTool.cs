using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Verifies a created field against the EXACT payload that was sent to Dataverse.
/// </summary>
public class VerifyFieldAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a created field against the exact plan that was executed. "
        + "Pass the planId from the most recent ExecuteCreateField result — the tool loads the "
        + "plan's expected values from server state and compares them against the actual field. "
        + "ALWAYS prefer this over verify_field_creation when you have a planId.")]
    public async Task<VerificationResult> VerifyFieldAgainstPlanAsync(
        [Description("planId of the field that was executed (from execute_create_field)")] string planId,
        [Description("Logical name of the table the field belongs to")] string tableLogicalName,
        CancellationToken cancellationToken)
    {
        // Defensive: reject planIds that clearly belong to a different verb. Otherwise
        // the agent passing the table's planId here would parse the table's SchemaName
        // as a field name and produce nonsense like "adc_phone.adc_phone NOT FOUND".
        // create_customer_lookup_* / create_polymorphic_lookup_* are accepted because
        // they ALSO produce a column on the referencing table — Tester needs a path
        // to verify those without inventing a fake FAIL. Their payload shape nests
        // the column metadata under "Lookup.*" instead of at the top level; we
        // handle both layouts below.
        var isLookupAction =
            planId.StartsWith("create_customer_lookup_", StringComparison.OrdinalIgnoreCase) ||
            planId.StartsWith("create_polymorphic_lookup_", StringComparison.OrdinalIgnoreCase);

        if (!planId.StartsWith("create_field_", StringComparison.OrdinalIgnoreCase) &&
            !planId.StartsWith("modify_field_", StringComparison.OrdinalIgnoreCase) &&
            !isLookupAction)
        {
            string hint;
            if (planId.StartsWith("create_global_option_set_", StringComparison.OrdinalIgnoreCase))
                hint = "Use verify_global_option_set_against_plan instead.";
            else if (planId.StartsWith("create_table_", StringComparison.OrdinalIgnoreCase))
                hint = "Use verify_table_against_plan instead.";
            else
                hint = "If you have no field planId for this component, the field was never planned/executed; do NOT report a fake FAIL — instead report 'no plan found, cannot verify'.";
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("planId", planId,
                    "WRONG PLAN TYPE — verify_field_against_plan only accepts create_field_*, modify_field_*, "
                    + $"create_customer_lookup_*, or create_polymorphic_lookup_* planIds. {hint}")],
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
        // CreateCustomerRelationships / CreatePolymorphicLookupAttribute payloads
        // wrap the column metadata under a "Lookup" property; create_field /
        // modify_field payloads put SchemaName at the top. Read whichever shape
        // applies so verification doesn't crash with KeyNotFound.
        var columnRoot = isLookupAction && plan.TryGetProperty("Lookup", out var lookupEl)
            ? lookupEl
            : plan;
        var schemaName = columnRoot.GetProperty("SchemaName").GetString() ?? string.Empty;
        var fieldLogicalName = schemaName.ToLowerInvariant();
        var loweredTable = tableLogicalName.ToLowerInvariant();

        var (actual, lastError) = await VerificationHelpers.RetryReadAsync(
            () => dataverseClient.GetFieldMetadataAsync(loweredTable, fieldLogicalName, cancellationToken), cancellationToken);
        if (actual is null)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("field", $"{loweredTable}.{fieldLogicalName}", $"NOT FOUND after retries: {lastError?.Message}")],
                Verdict: "FAIL");
        }

        var discrepancies = new List<Discrepancy>();

        if (columnRoot.TryGetProperty("DisplayName", out var dn) &&
            dn.TryGetProperty("LocalizedLabels", out var labels) &&
            labels.GetArrayLength() > 0 &&
            labels[0].TryGetProperty("Label", out var lbl))
        {
            VerificationHelpers.Compare(discrepancies, "DisplayName", lbl.GetString() ?? "", actual.DisplayName);
        }
        // For Customer/Polymorphic lookups the action determines RequiredLevel /
        // MaxLength / IsAuditEnabled at platform level — they aren't in the plan
        // payload, so skip those comparisons. Existence + DisplayName + AttributeType
        // are the meaningful checks.
        if (!isLookupAction)
        {
            if (columnRoot.TryGetProperty("RequiredLevel", out var rl) && rl.TryGetProperty("Value", out var rlVal))
                VerificationHelpers.Compare(discrepancies, "RequiredLevel", rlVal.GetString() ?? "", actual.RequiredLevel);
            if (columnRoot.TryGetProperty("MaxLength", out var ml) && actual.MaxLength.HasValue)
                VerificationHelpers.Compare(discrepancies, "MaxLength", ml.GetInt32().ToString(), actual.MaxLength.Value.ToString());
            if (columnRoot.TryGetProperty("IsAuditEnabled", out var ia) && ia.TryGetProperty("Value", out var iaVal))
                VerificationHelpers.Compare(discrepancies, "IsAuditEnabled", iaVal.GetBoolean().ToString(), actual.IsAuditEnabled.ToString());
        }
        else
        {
            // For lookups, verify the resulting column type. Customer columns
            // come back as "CustomerType" / "LookupType" in actual.AttributeType
            // depending on the action; treat both Lookup* forms as a match because
            // CreatePolymorphicLookupAttribute returns LookupType, and
            // CreateCustomerRelationships returns either CustomerType (newer envs)
            // or LookupType with a special target list (older).
            var actualType = actual.AttributeType ?? string.Empty;
            var ok = actualType.Contains("Lookup", StringComparison.OrdinalIgnoreCase)
                  || actualType.Contains("Customer", StringComparison.OrdinalIgnoreCase);
            if (!ok)
            {
                discrepancies.Add(new Discrepancy(
                    "AttributeType",
                    "Lookup or Customer",
                    actualType));
            }
        }

        var verdict = discrepancies.Count == 0 ? "PASS" : "FAIL";
        return new VerificationResult(
            Exists: true,
            Matches: discrepancies.Count == 0,
            Discrepancies: discrepancies,
            Verdict: verdict);
    }
}
