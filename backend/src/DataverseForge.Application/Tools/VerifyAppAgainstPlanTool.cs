using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tester verifier for model-driven apps. Loads the plan's stored payload from
/// <c>state.ExecutedPlans</c> (under create_app_* / update_app_*) and diffs
/// against the live app's name / description / clienttype / navigationtype.
/// </summary>
public class VerifyAppAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a model-driven app against the planned spec. Pass the planId from "
        + "plan_create_app or plan_update_app, plus the appId. Returns PASS / FAIL / PARTIAL "
        + "with discrepancies (name / description / clienttype / navigationtype). FAIL if the "
        + "name doesn't match (a primary attribute); PARTIAL if only secondary attributes drift.")]
    public async Task<AppVerificationResult> VerifyAppAgainstPlanAsync(
        [Description("planId from plan_create_app or plan_update_app.")] string planId,
        [Description("appmoduleid (GUID) of the app to verify.")] string appId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(appId, out var parsedId) || parsedId == Guid.Empty)
        {
            return new AppVerificationResult(
                Guid.Empty, planId, "FAIL",
                new List<string> { $"appId '{appId}' is not a valid GUID." }.AsReadOnly());
        }

        if (!(planId.StartsWith("create_app_", StringComparison.Ordinal)
              || planId.StartsWith("update_app_", StringComparison.Ordinal)))
        {
            return new AppVerificationResult(
                parsedId, planId, "FAIL",
                new List<string> { $"WRONG PLAN TYPE: '{planId}' is not an app plan. Use the matching verify_*_against_plan instead." }.AsReadOnly());
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!state.ExecutedPlans.TryGetValue(planId, out var plannedPayload))
        {
            var pendingHint = state.PendingPlans.ContainsKey(planId)
                ? " The plan is still pending — execute_*_app hasn't been called yet."
                : string.Empty;
            return new AppVerificationResult(
                parsedId, planId, "FAIL",
                new List<string> { $"Plan '{planId}' not found in ExecutedPlans.{pendingHint}" }.AsReadOnly());
        }

        AppModuleDetails? live;
        try
        {
            // Eventual-consistency retry: real session 2026-05-05 had a freshly-
            // created appmodule return 404 from GET /appmodules({id}) for several
            // seconds after the create succeeded (and after PublishApp could
            // already see it). The maker portal showed the app, but the direct
            // read by id missed it. Retry up to 3 times with backoff so a
            // transient null doesn't false-FAIL the verifier.
            live = await ReadWithRetryAsync(parsedId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AppVerificationResult(
                parsedId, planId, "FAIL",
                new List<string> { $"Could not fetch app {parsedId}: {ex.Message}" }.AsReadOnly());
        }

        if (live is null)
        {
            return new AppVerificationResult(
                parsedId, planId, "FAIL",
                new List<string> { $"App {parsedId} not found in the environment after retries (eventual consistency window exceeded). The create may have actually succeeded — check the maker portal directly." }.AsReadOnly());
        }

        var planned = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(plannedPayload)
            ?? new Dictionary<string, JsonElement>();

        var discrepancies = new List<string>();

        var plannedName = TryGetString(planned, "name");
        if (plannedName is not null && !string.Equals(plannedName, live.Name, StringComparison.Ordinal))
            discrepancies.Add($"name: expected '{plannedName}', got '{live.Name}'");

        // uniquename only present in create_app payload (update_app intentionally
        // doesn't ship it — Dataverse rejects uniquename PATCH).
        var plannedUniqueName = TryGetString(planned, "uniquename");
        if (plannedUniqueName is not null && !string.Equals(plannedUniqueName, live.UniqueName, StringComparison.Ordinal))
            discrepancies.Add($"uniquename: expected '{plannedUniqueName}', got '{live.UniqueName}'");

        var plannedDescription = TryGetString(planned, "description");
        if (plannedDescription is not null && !string.Equals(plannedDescription, live.Description, StringComparison.Ordinal))
            discrepancies.Add($"description: expected '{plannedDescription}', got '{live.Description}'");

        var plannedClientType = TryGetInt(planned, "clienttype");
        if (plannedClientType is not null && plannedClientType.Value != live.ClientType)
            discrepancies.Add($"clienttype: expected {plannedClientType}, got {live.ClientType}");

        var plannedNavigationType = TryGetInt(planned, "navigationtype");
        if (plannedNavigationType is not null && plannedNavigationType.Value != live.NavigationType)
            discrepancies.Add($"navigationtype: expected {plannedNavigationType}, got {live.NavigationType}");

        var verdict = discrepancies.Count == 0 ? "PASS"
            : discrepancies.Any(d => d.StartsWith("name:") || d.StartsWith("uniquename:")) ? "FAIL"
            : "PARTIAL";

        return new AppVerificationResult(
            AppId: parsedId,
            PlanId: planId,
            Verdict: verdict,
            Discrepancies: discrepancies.AsReadOnly());
    }

    /// <summary>
    /// Backoff schedule for <see cref="ReadWithRetryAsync"/>. Three attempts
    /// at 0ms / 2s / 5s = up to ~7s of additional eventual-consistency
    /// tolerance. Tuned up from 4s after a real session showed GetAppAsync
    /// returning null even 10s after a successful create. Tests set this to
    /// all-zeros to avoid real-time waits.
    /// </summary>
    internal static int[] RetryDelaysMs = [0, 2000, 5000];

    /// <summary>
    /// Read an app by id with backoff retries on null, plus a list-based
    /// fallback. Real session 2026-05-06: GetAppAsync(id) returned null for
    /// the freshly-created (unpublished) "Forge Phase3 Demo" app, but
    /// GetAppsList DID include it. The fallback hits the list endpoint and
    /// matches by appId so the verifier can still get a real comparison
    /// instead of false-FAILing on the eventual-consistency window.
    /// </summary>
    private async Task<AppModuleDetails?> ReadWithRetryAsync(Guid appId, CancellationToken cancellationToken)
    {
        AppModuleDetails? live = null;
        foreach (var delay in RetryDelaysMs)
        {
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }
            live = await dataverseClient.GetAppAsync(appId, cancellationToken);
            if (live is not null) return live;
        }

        // Fallback: scan the apps list. The list endpoint surfaces newly-
        // created apps faster than the direct GET-by-id in some Dataverse
        // configurations. Project the list row into AppModuleDetails so the
        // verifier's downstream diff still works (descriptor/configxml are
        // null on this path; verifier only diffs name/uniquename/description/
        // clienttype/navigationtype, which the list row carries).
        try
        {
            var list = await dataverseClient.GetAppsListAsync(includeUnpublished: true, cancellationToken);
            var match = list.FirstOrDefault(a => a.AppModuleId == appId);
            if (match is not null)
            {
                return new AppModuleDetails(
                    match.AppModuleId, match.Name, match.UniqueName, match.Description,
                    match.WebResourceId, match.ClientType, match.NavigationType,
                    match.StateCode, match.StatusCode, match.IsManaged, match.PublishedOn,
                    Descriptor: null, ConfigXml: null);
            }
        }
        catch { /* swallow — we're already in the failure path */ }

        return null;
    }

    private static string? TryGetString(IReadOnlyDictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static int? TryGetInt(IReadOnlyDictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
    }
}
