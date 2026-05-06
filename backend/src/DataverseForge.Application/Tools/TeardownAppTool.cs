using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Reverse of <see cref="ScaffoldAppTool"/>: unassign all roles and delete
/// the app. Tables and any other components survive — only the
/// scaffold-specific artifacts (the app + its auto-generated sitemap, via
/// the appmodule's cascade) are removed.
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
    [Description("Tear down a model-driven app: unassign every role currently linked to it, then "
        + "delete the appmodule (cascades the appmodulecomponent join rows + the app's sitemap "
        + "association). Tables/forms/views/charts the app referenced are NOT deleted — only the "
        + "app-specific glue. High-impact (orange) — request_approval before calling. Returns a "
        + "per-step result mirroring scaffold_app's shape.")]
    public async Task<ScaffoldAppResult> TeardownAppAsync(
        [Description("appmoduleid (GUID) of the app to tear down.")] string appId,
        CancellationToken cancellationToken)
    {
        var result = new ScaffoldAppResult();

        if (!Guid.TryParse(appId, out var parsedAppId) || parsedAppId == Guid.Empty)
        {
            result.AddFailure("validate", $"appId '{appId}' is not a valid GUID.");
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

        // Step 3 — delete the app. Dataverse cascades the
        // appmodule_appmodulecomponent rows + the appmodule_sitemap
        // association, so we don't need an explicit sitemap delete.
        try
        {
            await dataverseClient.DeleteAppAsync(parsedAppId, cancellationToken);
            result.AddSuccess("delete_app", $"App {parsedAppId} deleted (components and sitemap association cascaded).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "TeardownApp delete_app failed");
            result.AddFailure("delete_app", ex.Message);
            return result; // hard fail — the app is still there, scaffolds shouldn't be marked torn-down
        }

        // Forget the app from session state. The scaffold's reverse-op record
        // in OperationStack stays for audit; we just stop tracking the app
        // as live.
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.Apps.RemoveAll(a => a.AppModuleId == parsedAppId);
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

        return result;
    }
}
