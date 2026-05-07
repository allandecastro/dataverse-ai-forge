using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Reverse of <see cref="ScaffoldAppTool"/>: unassign all roles, delete the
/// appmodule, and (if scaffold tracked the sitemap in <c>state.SiteMaps</c>)
/// also delete the sitemap row. Tables and any other components survive.
///
/// Why the explicit sitemap delete: the <c>appmodule</c> cascade clears
/// only the <c>appmodule_sitemap</c> M:N association — the sitemap row
/// itself outlives the app and blocks a future scaffold with the same
/// uniqueName ("duplicate sitemapnameunique"). Skipping the delete when
/// no scaffold record exists in state means we won't nuke a user-attached
/// sitemap they manually wired up.
///
/// High-impact (orange). Direct (no plan/execute split) but destructive — the
/// agent should request_approval before calling.
/// </summary>
public class TeardownAppTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<TeardownAppTool> logger)
{
    [Description("Tear down a model-driven app: unassign every role linked to it, delete the "
        + "appmodule (cascades the appmodulecomponent join rows), and — if the sitemap was "
        + "scaffold-created and recorded in state.SiteMaps for this appId — also delete the "
        + "sitemap row so a re-scaffold won't collide on duplicate sitemapnameunique. Tables / "
        + "forms / views / charts referenced by the app are NOT deleted. High-impact (orange) "
        + "— request_approval before calling. Accepts EITHER the appmodule GUID or the app's "
        + "uniqueName (e.g. 'eppc_forgedemo') — passing a uniqueName is preferred since it "
        + "sidesteps GUID-typo errors that real-session 2026-05-07 23:51 hit (a one-char typo "
        + "produced a 400 'AppModule not found').")]
    public async Task<ScaffoldAppResult> TeardownAppAsync(
        [Description("appmodule GUID or uniqueName (e.g. 'eppc_forgedemo'). uniqueName preferred.")] string appIdOrUniqueName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = new ScaffoldAppResult();

        // Accept either form: parse as GUID first; otherwise look up by
        // uniqueName via GetAppsList (more forgiving for the agent — GUIDs
        // are 36 chars and easily corrupted across LLM tool-call boundaries).
        Guid parsedAppId;
        if (Guid.TryParse(appIdOrUniqueName, out parsedAppId) && parsedAppId != Guid.Empty)
        {
            // Looks like a GUID — surface a clearer error than "AppModule not
            // found" if it's not actually present (agents typo the last few
            // chars regularly). But:
            //   - tolerate the eventual-consistency window: a freshly-created
            //     app (especially one whose initial publish failed) can be
            //     invisible to GetAppAsync for several minutes even though
             //    the maker portal sees it. Use the same retry + list
             //    fallback pattern as VerifyAppAgainstPlanTool.
             //   - Real session: scaffold → publish failed → user immediately
             //     calls teardown; pre-validate hard-failed with "no app
             //     with that id exists" because GetAppAsync(id) was still
             //     blind. The user had a perfectly valid GUID.
            try
            {
                var live = await ReadAppWithRetryAsync(parsedAppId, cancellationToken);
                if (live is null)
                {
                    result.AddFailure("validate", $"appId '{appIdOrUniqueName}' parsed as GUID but no app with that id exists (looked it up directly and via the apps list, with retries). Common cause: a typo in the GUID (off-by-one-char on the last segment is the recurring failure mode). Try passing the uniqueName instead (e.g. 'eppc_yourapp').");
                    return result;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "TeardownApp pre-validate read failed for {AppId} — continuing with delete attempt", parsedAppId);
            }
        }
        else if (!string.IsNullOrWhiteSpace(appIdOrUniqueName))
        {
            // Treat as uniqueName — look it up in the apps list.
            try
            {
                var apps = await dataverseClient.GetAppsListAsync(includeUnpublished: true, cancellationToken);
                var match = apps.FirstOrDefault(a => string.Equals(a.UniqueName, appIdOrUniqueName, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    result.AddFailure("validate", $"No app with uniqueName '{appIdOrUniqueName}' found in this environment. Pass the exact uniqueName from get_apps_list (e.g. 'eppc_forgedemo') or the appmodule GUID.");
                    return result;
                }
                parsedAppId = match.AppModuleId;
                result.AddSuccess("resolve_uniquename", $"Resolved uniqueName '{appIdOrUniqueName}' → appId={parsedAppId}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "TeardownApp uniqueName lookup failed");
                result.AddFailure("validate", $"Could not look up app by uniqueName '{appIdOrUniqueName}': {ex.Message}");
                return result;
            }
        }
        else
        {
            result.AddFailure("validate", "appIdOrUniqueName is required (pass either a GUID or a uniqueName like 'eppc_forgedemo').");
            return result;
        }
        result.AppId = parsedAppId;

        // Step 1 — list current role assignments so we can unassign each one.
        // Done first so a delete-without-unassign doesn't leave orphan
        // appmoduleroles rows in some Dataverse configurations.
        IReadOnlyList<AppModuleRoleInfo> roles;
        try
        {
            roles = await dataverseClient.ListAppRolesAsync(parsedAppId, cancellationToken);
            result.AddSuccess("list_roles", $"Found {roles.Count} role assignment(s).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "TeardownApp list_roles failed (continuing with delete anyway)");
            result.AddFailure("list_roles", ex.Message);
            roles = Array.Empty<AppModuleRoleInfo>();
        }

        // Step 2 — unassign each role. Best-effort; one failure shouldn't
        // block the delete since the appmodule cascade typically handles
        // orphan rows anyway.
        foreach (var role in roles)
        {
            try
            {
                await dataverseClient.UnassignAppRoleAsync(parsedAppId, role.RoleId, cancellationToken);
                result.AddSuccess($"unassign_role:{role.RoleId}", $"Unassigned role '{role.Name}'.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "TeardownApp unassign_role failed for {RoleId}", role.RoleId);
                result.AddFailure($"unassign_role:{role.RoleId}", ex.Message);
            }
        }

        // Step 3 — capture any scaffold-tracked sitemap BEFORE deleting the
        // app. We need the sitemap id to delete it explicitly in step 5
        // (the cascade clears only the M:N association, not the sitemap row).
        var stateBefore = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var trackedSitemap = stateBefore.SiteMaps.FirstOrDefault(s => s.AttachedToAppId == parsedAppId);

        // Step 4 — delete the app. Cascades the appmodulecomponent rows and
        // the appmodule_sitemap association — but the sitemap ROW itself
        // survives, hence step 5.
        try
        {
            await dataverseClient.DeleteAppAsync(parsedAppId, cancellationToken);
            result.AddSuccess("delete_app", $"App {parsedAppId} deleted (component associations cascaded).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "TeardownApp delete_app failed");
            result.AddFailure("delete_app", ex.Message);
            return result; // hard fail — the app is still there, scaffolds shouldn't be marked torn-down
        }

        // Step 5 — delete the scaffold-tracked sitemap row. Skip if no
        // scaffold record exists for this app (don't nuke a user-attached
        // sitemap that wasn't ours to delete).
        if (trackedSitemap is not null)
        {
            try
            {
                await dataverseClient.DeleteSiteMapAsync(trackedSitemap.SiteMapId, cancellationToken);
                result.AddSuccess("delete_sitemap", $"Sitemap '{trackedSitemap.SiteMapNameUnique}' (id={trackedSitemap.SiteMapId}) deleted.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "TeardownApp delete_sitemap failed for {SiteMapId}", trackedSitemap.SiteMapId);
                result.AddFailure("delete_sitemap", $"Sitemap row {trackedSitemap.SiteMapId} could not be deleted: {ex.Message}. Re-scaffold with the same uniqueName may fail until this is cleaned up manually in the maker portal.");
            }
        }
        else
        {
            // Soft-info, not a failure — the user's flow may legitimately not
            // have a scaffold-created sitemap (e.g. they manually attached
            // their own pre-existing sitemap). Surface so the agent can
            // mention "we left the sitemap alone".
            result.AddSuccess("delete_sitemap", "No scaffold-tracked sitemap for this app — skipped (any pre-existing sitemap left intact).");
        }

        // Forget the app from session state. The scaffold's reverse-op record
        // in OperationStack stays for audit; we just stop tracking the app
        // as live.
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.Apps.RemoveAll(a => a.AppModuleId == parsedAppId);
        state.SiteMaps.RemoveAll(s => s.AttachedToAppId == parsedAppId);
        state.AuditLog.Add(new AuditEntry
        {
            EntryId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            AgentName = AgentNames.UIBuilder,
            Action = "TeardownApp",
            Detail = $"Tore down app {parsedAppId} ({roles.Count} role(s) unassigned, app deleted).",
            Success = result.OverallSucceeded,
        });
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        sw.Stop();
        ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("tool.name", "TeardownApp"),
            new KeyValuePair<string, object?>("tool.status", result.OverallSucceeded ? "success" : "partial"),
            new KeyValuePair<string, object?>("teardown.roles", roles.Count));
        return result;
    }

    /// <summary>
    /// Backoff schedule for <see cref="ReadAppWithRetryAsync"/>: same shape
    /// as <see cref="VerifyAppAgainstPlanTool.RetryDelaysMs"/>. Tests
    /// override this to all-zeros to avoid real-time waits.
    /// </summary>
    internal static int[] RetryDelaysMs = [0, 2000, 5000];

    /// <summary>
    /// Try to read an app by id with backoff retries on null, plus a
    /// list-based fallback. Mirrors <c>VerifyAppAgainstPlanTool.ReadWithRetryAsync</c> —
    /// kept inline here because the dependency goes the wrong way for
    /// extracting a shared helper today (Verify lives next to plan tools,
    /// Teardown is its own surface). If a third caller appears, promote.
    /// </summary>
    private async Task<AppModuleInfo?> ReadAppWithRetryAsync(Guid appId, CancellationToken cancellationToken)
    {
        foreach (var delay in RetryDelaysMs)
        {
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }
            var live = await dataverseClient.GetAppAsync(appId, cancellationToken);
            if (live is not null)
            {
                // Project AppModuleDetails → AppModuleInfo so callers don't
                // care which path produced the row. We only need it to confirm
                // existence; nothing further looks at the projected fields.
                return new AppModuleInfo(
                    live.AppModuleId, live.Name, live.UniqueName, live.Description,
                    live.WebResourceId, live.ClientType, live.NavigationType,
                    live.StateCode, live.StatusCode, live.IsManaged, live.PublishedOn);
            }
        }

        // Fallback: scan the apps list (sometimes faster than GET-by-id for
        // freshly-created or publish-failed apps).
        try
        {
            var list = await dataverseClient.GetAppsListAsync(includeUnpublished: true, cancellationToken);
            return list.FirstOrDefault(a => a.AppModuleId == appId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "TeardownApp ReadAppWithRetry list-fallback failed for {AppId}", appId);
            return null;
        }
    }
}
