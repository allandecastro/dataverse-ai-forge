using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class VerifyViewAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a Dataverse system view against the planned spec. Loads the planned "
        + "OData payload from server-side state (under the planId returned by plan_create_view "
        + "or plan_update_view) and compares against the live savedquery fetched via the table's "
        + "view list. Returns PASS / FAIL with concrete discrepancies. PlanId must start with "
        + "'create_view_' or 'update_view_'. ALWAYS prefer this over verify_view_creation when "
        + "you have a planId.")]
    public async Task<VerificationResult> VerifyViewAgainstPlanAsync(
        [Description("planId from plan_create_view or plan_update_view")] string planId,
        [Description("The view's GUID (savedqueryid) — from execute_create_view / execute_update_view result")]
        string viewId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(viewId, out var parsedViewId) || parsedViewId == Guid.Empty)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("viewId", viewId, "not a valid GUID")],
                Verdict: "FAIL");
        }

        if (planId.StartsWith("rejected_view_invalid_", StringComparison.Ordinal))
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("plan", planId,
                    "REJECTED PLAN: this planId was rejected at planning time and never reached "
                    + "Dataverse — re-plan with valid inputs (do NOT pass this planId to verify).")],
                Verdict: "FAIL");
        }

        if (!(planId.StartsWith("create_view_", StringComparison.Ordinal)
              || planId.StartsWith("update_view_", StringComparison.Ordinal)))
        {
            string suggestion = planId switch
            {
                _ when planId.StartsWith("create_form_") || planId.StartsWith("update_form_") => "verify_form_against_plan",
                _ when planId.StartsWith("create_table_") || planId.StartsWith("update_table_") => "verify_table_against_plan",
                _ when planId.StartsWith("create_field_") || planId.StartsWith("update_field_") => "verify_field_against_plan",
                _ when planId.StartsWith("create_chart_") || planId.StartsWith("update_chart_") => "verify_chart_against_plan",
                _ => "the appropriate verify_*_against_plan tool",
            };
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("planId", planId,
                    $"WRONG PLAN TYPE — verify_view_against_plan only accepts create_view_* or "
                    + $"update_view_* planIds. Use {suggestion} instead.")],
                Verdict: "FAIL");
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!state.ExecutedPlans.TryGetValue(planId, out var plannedPayload))
        {
            var pendingHint = state.PendingPlans.ContainsKey(planId)
                ? " The plan is still pending — execute_create_view / execute_update_view hasn't been called yet."
                : string.Empty;
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("plan", planId, $"NOT FOUND in ExecutedPlans.{pendingHint}")],
                Verdict: "FAIL");
        }

        var plan = JsonDocument.Parse(plannedPayload).RootElement;
        var tableLogical = plan.TryGetProperty("returnedtypecode", out var rtEl)
            ? rtEl.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(tableLogical))
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("plan", planId,
                    "PLAN MISSING returnedtypecode — cannot resolve which table to query for the view list.")],
                Verdict: "FAIL");
        }

        IReadOnlyList<ViewInfo> liveViews;
        try
        {
            liveViews = await dataverseClient.GetViewsListAsync(tableLogical, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("view", parsedViewId.ToString(), $"view list fetch failed for table '{tableLogical}': {ex.Message}")],
                Verdict: "FAIL");
        }

        var live = liveViews.FirstOrDefault(v => v.ViewId == parsedViewId);
        if (live is null)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("view", parsedViewId.ToString(),
                    $"NOT FOUND on table '{tableLogical}' (savedquery rows enumerated: {liveViews.Count})")],
                Verdict: "FAIL");
        }

        var discrepancies = new List<Discrepancy>();

        if (plan.TryGetProperty("name", out var nameEl))
        {
            var expectedName = nameEl.GetString() ?? string.Empty;
            if (!string.Equals(expectedName, live.Name, StringComparison.OrdinalIgnoreCase))
            {
                discrepancies.Add(new Discrepancy("name", expectedName, live.Name));
            }
        }

        if (plan.TryGetProperty("fetchxml", out var fetchEl) && fetchEl.ValueKind == JsonValueKind.String)
        {
            var expected = NormalizeXml(fetchEl.GetString() ?? string.Empty);
            var actual = NormalizeXml(live.FetchXml);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                discrepancies.Add(new Discrepancy(
                    "fetchxml",
                    $"len={expected.Length}",
                    $"len={actual.Length} (whitespace-normalized content differs)"));
            }
        }

        if (plan.TryGetProperty("layoutxml", out var layoutEl) && layoutEl.ValueKind == JsonValueKind.String)
        {
            var expected = NormalizeXml(layoutEl.GetString() ?? string.Empty);
            var actual = NormalizeXml(live.LayoutXml);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                discrepancies.Add(new Discrepancy(
                    "layoutxml",
                    $"len={expected.Length}",
                    $"len={actual.Length} (whitespace-normalized content differs)"));
            }
        }

        return new VerificationResult(
            Exists: true,
            Matches: discrepancies.Count == 0,
            Discrepancies: discrepancies,
            Verdict: discrepancies.Count == 0 ? "PASS" : "FAIL");
    }

    // Dataverse injects savedqueryid="<guid>" onto the <fetch> element when it
    // echoes back fetchxml. Strip it (and a small set of related server-side
    // attributes) before comparing — otherwise a functionally-identical view
    // FAILs on byte-level XML diff.
    private static string NormalizeXml(string xml)
    {
        var s = xml ?? string.Empty;
        s = ServerInjectedAttributes.Replace(s, string.Empty);
        s = WhitespaceBetweenTags.Replace(s, "><");
        return s.Trim();
    }

    private static readonly Regex WhitespaceBetweenTags = new(
        @">\s+<", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ServerInjectedAttributes = new(
        @"\s+(savedqueryid|userqueryid|distinct)=""[^""]*""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
