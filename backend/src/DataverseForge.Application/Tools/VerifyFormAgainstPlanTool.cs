using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class VerifyFormAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a Dataverse form against the planned spec. Loads the planned OData "
        + "payload from server-side state (under the planId returned by plan_create_form or "
        + "plan_update_form) and compares against the live systemform fetched by formId. "
        + "Returns PASS / FAIL with concrete discrepancies. PlanId must start with "
        + "'create_form_' or 'update_form_'; any other prefix returns a clear WRONG PLAN TYPE "
        + "error. ALWAYS prefer this over verify_form_creation when you have a planId.")]
    public async Task<VerificationResult> VerifyFormAgainstPlanAsync(
        [Description("planId from plan_create_form or plan_update_form")] string planId,
        [Description("The form's GUID (formid) — from execute_create_form / execute_update_form result")]
        string formId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(formId, out var parsedFormId) || parsedFormId == Guid.Empty)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("formId", formId, "not a valid GUID")],
                Verdict: "FAIL");
        }

        if (planId.StartsWith("rejected_form_invalid_", StringComparison.Ordinal))
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("plan", planId,
                    "REJECTED PLAN: this planId was rejected at planning time and never reached "
                    + "Dataverse — re-plan with valid inputs (do NOT pass this planId to verify).")],
                Verdict: "FAIL");
        }

        if (!(planId.StartsWith("create_form_", StringComparison.Ordinal)
              || planId.StartsWith("update_form_", StringComparison.Ordinal)))
        {
            string suggestion = planId switch
            {
                _ when planId.StartsWith("create_view_") || planId.StartsWith("update_view_") => "verify_view_against_plan",
                _ when planId.StartsWith("create_table_") || planId.StartsWith("update_table_") => "verify_table_against_plan",
                _ when planId.StartsWith("create_field_") || planId.StartsWith("update_field_") => "verify_field_against_plan",
                _ when planId.StartsWith("create_chart_") || planId.StartsWith("update_chart_") => "verify_chart_against_plan",
                _ => "the appropriate verify_*_against_plan tool",
            };
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("planId", planId,
                    $"WRONG PLAN TYPE — verify_form_against_plan only accepts create_form_* or "
                    + $"update_form_* planIds. Use {suggestion} instead.")],
                Verdict: "FAIL");
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!state.ExecutedPlans.TryGetValue(planId, out var plannedPayload))
        {
            var pendingHint = state.PendingPlans.ContainsKey(planId)
                ? " The plan is still pending — execute_create_form / execute_update_form hasn't been called yet."
                : string.Empty;
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("plan", planId, $"NOT FOUND in ExecutedPlans.{pendingHint}")],
                Verdict: "FAIL");
        }

        // Pull the live form record from Dataverse.
        FormDefinition? live;
        try
        {
            live = await dataverseClient.GetFormDefinitionAsync(parsedFormId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("form", parsedFormId.ToString(), $"read failed: {ex.Message}")],
                Verdict: "FAIL");
        }
        if (live is null)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("form", parsedFormId.ToString(), "NOT FOUND in environment")],
                Verdict: "FAIL");
        }

        var plan = JsonDocument.Parse(plannedPayload).RootElement;
        var discrepancies = new List<Discrepancy>();

        if (plan.TryGetProperty("name", out var nameEl))
        {
            var expectedName = nameEl.GetString() ?? string.Empty;
            if (!string.Equals(expectedName, live.Name, StringComparison.OrdinalIgnoreCase))
            {
                discrepancies.Add(new Discrepancy("name", expectedName, live.Name));
            }
        }

        // type is a numeric code in the OData payload (Main=2, QuickCreate=7,
        // QuickView=6, Card=11). FormDefinition.FormType is the friendly string.
        if (plan.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.Number)
        {
            var expectedFriendly = MapFormTypeCode(typeEl.GetInt32());
            if (!string.Equals(expectedFriendly, live.FormType, StringComparison.OrdinalIgnoreCase))
            {
                discrepancies.Add(new Discrepancy("type", expectedFriendly, live.FormType));
            }
        }

        if (plan.TryGetProperty("formxml", out var xmlEl) && xmlEl.ValueKind == JsonValueKind.String)
        {
            var expectedXml = NormalizeXml(xmlEl.GetString() ?? string.Empty);
            var actualXml = NormalizeXml(live.FormXml);
            if (!string.Equals(expectedXml, actualXml, StringComparison.Ordinal))
            {
                discrepancies.Add(new Discrepancy(
                    "formxml",
                    $"len={expectedXml.Length}",
                    $"len={actualXml.Length} (whitespace-normalized content differs)"));
            }
        }

        return new VerificationResult(
            Exists: true,
            Matches: discrepancies.Count == 0,
            Discrepancies: discrepancies,
            Verdict: discrepancies.Count == 0 ? "PASS" : "FAIL");
    }

    // Maps systemform.type code → FormDefinition.FormType friendly string.
    private static string MapFormTypeCode(int typeCode) => typeCode switch
    {
        2 => "Main",
        6 => "QuickView",
        7 => "QuickCreate",
        11 => "Card",
        _ => $"Type({typeCode})",
    };

    private static string NormalizeXml(string xml) =>
        WhitespaceBetweenTags.Replace(xml ?? string.Empty, "><").Trim();

    private static readonly Regex WhitespaceBetweenTags = new(
        @">\s+<", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
