using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tester verifier for sitemaps. Loads the plan's stored payload from
/// <c>state.ExecutedPlans</c> (under create_sitemap_* / update_sitemap_*) and
/// diffs the planned XML against the live sitemap's <c>sitemapxml</c> blob,
/// whitespace-normalized.
/// </summary>
public class VerifySiteMapAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a Dataverse sitemap against the planned spec. Pass the planId from "
        + "plan_create_sitemap or plan_update_sitemap, plus the appId the sitemap is attached to "
        + "(verifier reads the live sitemap via get_sitemap_for_app). Returns PASS / FAIL / "
        + "PARTIAL with the XML diff (whitespace-normalized).")]
    public async Task<SiteMapVerificationResult> VerifySiteMapAgainstPlanAsync(
        [Description("planId from plan_create_sitemap or plan_update_sitemap.")] string planId,
        [Description("appmoduleid (GUID) — the verifier reads the sitemap via the app's M:N association.")] string appId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(appId, out var parsedAppId) || parsedAppId == Guid.Empty)
        {
            return new SiteMapVerificationResult(
                Guid.Empty, planId, "FAIL",
                new List<string> { $"appId '{appId}' is not a valid GUID." }.AsReadOnly(),
                null, null);
        }

        if (!(planId.StartsWith("create_sitemap_", StringComparison.Ordinal)
              || planId.StartsWith("update_sitemap_", StringComparison.Ordinal)))
        {
            return new SiteMapVerificationResult(
                Guid.Empty, planId, "FAIL",
                new List<string> { $"WRONG PLAN TYPE: '{planId}' is not a sitemap plan." }.AsReadOnly(),
                null, null);
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!state.ExecutedPlans.TryGetValue(planId, out var plannedPayload))
        {
            var pendingHint = state.PendingPlans.ContainsKey(planId)
                ? " The plan is still pending — execute_*_sitemap hasn't been called yet."
                : string.Empty;
            return new SiteMapVerificationResult(
                Guid.Empty, planId, "FAIL",
                new List<string> { $"Plan '{planId}' not found in ExecutedPlans.{pendingHint}" }.AsReadOnly(),
                null, null);
        }

        SiteMapInfo? live;
        try
        {
            live = await dataverseClient.GetSiteMapForAppAsync(parsedAppId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SiteMapVerificationResult(
                Guid.Empty, planId, "FAIL",
                new List<string> { $"Could not fetch sitemap for app {parsedAppId}: {ex.Message}" }.AsReadOnly(),
                null, null);
        }

        if (live is null)
        {
            return new SiteMapVerificationResult(
                Guid.Empty, planId, "FAIL",
                new List<string> { $"App {parsedAppId} has no sitemap attached. Did attach_sitemap_to_app run successfully?" }.AsReadOnly(),
                null, null);
        }

        var planned = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(plannedPayload)
            ?? new Dictionary<string, JsonElement>();

        var discrepancies = new List<string>();

        var plannedXml = TryGetString(planned, "sitemapxml") ?? string.Empty;
        if (!XmlEquivalent(plannedXml, live.SiteMapXml))
        {
            discrepancies.Add("sitemapxml differs from plan (whitespace-normalized).");
        }

        var verdict = discrepancies.Count == 0 ? "PASS" : "FAIL";

        return new SiteMapVerificationResult(
            SiteMapId: live.SiteMapId,
            PlanId: planId,
            Verdict: verdict,
            Discrepancies: discrepancies.AsReadOnly(),
            PlanSiteMapXml: plannedXml,
            ActualSiteMapXml: live.SiteMapXml);
    }

    private static string? TryGetString(IReadOnlyDictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static bool XmlEquivalent(string a, string b) => Normalize(a) == Normalize(b);

    /// <summary>
    /// Whitespace-normalize for diffing. Collapses consecutive whitespace to a
    /// single space, then strips whitespace around tag boundaries
    /// (<c>&gt; &lt;</c> → <c>&gt;&lt;</c>) so a Dataverse-formatted live blob
    /// equates to a builder-emitted plan blob despite different indentation.
    /// </summary>
    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var collapsed = Regex.Replace(s, @"\s+", " ");
        // Strip whitespace around tag boundaries: "> <" → "><"
        var tightened = Regex.Replace(collapsed, @">\s+<", "><");
        return tightened.Trim();
    }
}
