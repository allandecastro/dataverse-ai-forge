using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// One-shot orchestrator: create a working model-driven app from a list of
/// table logical names + (optional) role IDs. Composes the lower-level
/// pieces shipped in Phases 2 / 3 / 4 / 5 — the user approves ONE high-risk
/// (orange) action and Forge runs the whole sequence:
///
///   1. Resolve table logical names → MetadataIds.
///   2. Create the appmodule (atomic with active solution).
///   3. Build a default sitemap (one Area, one Group, one SubArea per table)
///      → render XML → create sitemap (atomic with active solution).
///   4. Attach sitemap + entities to the app.
///   5. Publish the app.
///   6. Assign each requested role.
///
/// Each step's success/failure is reported in <see cref="ScaffoldAppResult"/>
/// so the agent can quote precise failures back to the user. Steps after a
/// hard failure (couldn't even create the app) short-circuit; soft failures
/// (one role assignment fails) don't abort the whole flow.
/// </summary>
public class ScaffoldAppTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ScaffoldAppTool> logger)
{
    /// <summary>
    /// OOB icon web resource that ships with every Dataverse environment.
    /// Same default <c>plan_create_app</c> uses; resolved at scaffold time
    /// because the GUID varies per environment.
    /// </summary>
    private const string DefaultAppIconWebResourceName = "msdyn_/Images/AppModule_Default_Icon.png";

    [Description("Build a working model-driven app from a name + a list of table logical names + "
        + "(optional) role IDs in ONE call. Creates the app, auto-generates a sitemap (one Area, one "
        + "Group, one SubArea per table), attaches sitemap + entities, publishes, and assigns roles. "
        + "High-impact (orange) — request_approval BEFORE calling. Returns a per-step result so you "
        + "can quote any specific step failure verbatim. Use this when the user wants 'a working app "
        + "for these tables' without specifying every step.")]
    public async Task<ScaffoldAppResult> ScaffoldAppAsync(
        [Description("Display name (e.g., 'Gift Hub').")] string name,
        [Description("Unique name with publisher prefix (e.g., 'eppc_gifthub'). Lowercased automatically.")] string uniqueName,
        [Description("Description shown in the app picker.")] string description,
        [Description("Comma-separated list of entity logical names to include (e.g., 'account,contact,eppc_gift'). At least one required.")]
        string tableLogicalNames,
        [Description("Optional comma-separated role IDs (GUIDs) to assign. Leave empty for none.")]
        string? roleIds,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = new ScaffoldAppResult();

        if (string.IsNullOrWhiteSpace(name))
        {
            result.AddFailure("validate", "name is required.");
            return result;
        }
        if (string.IsNullOrWhiteSpace(uniqueName))
        {
            result.AddFailure("validate", "uniqueName is required.");
            return result;
        }
        if (string.IsNullOrWhiteSpace(tableLogicalNames))
        {
            result.AddFailure("validate", "At least one table logical name is required.");
            return result;
        }

        var normalizedUniqueName = uniqueName.Trim().ToLowerInvariant();
        var tables = tableLogicalNames
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (tables.Count == 0)
        {
            result.AddFailure("validate", "tableLogicalNames parsed to an empty list — pass at least one logical name.");
            return result;
        }
        var roles = ParseRoleIds(roleIds);
        result.AddSuccess("validate", $"Parsed {tables.Count} table(s) and {roles.Count} role(s).");

        // Step 1 — resolve table logical names → MetadataIds in parallel.
        // Hard fail if any table doesn't exist; cheaper than partially building
        // an app that's missing its primary entities.
        var resolveTasks = tables.Select(t => ResolveEntityAsync(t, cancellationToken)).ToList();
        var resolved = await Task.WhenAll(resolveTasks);
        var unknown = resolved.Where(r => r.MetadataId is null).Select(r => r.LogicalName).ToList();
        if (unknown.Count > 0)
        {
            result.AddFailure("resolve_tables", $"Unknown entity/entities: {string.Join(", ", unknown)}.");
            return result;
        }
        result.AddSuccess("resolve_tables", $"Resolved {resolved.Length} entity MetadataId(s).");

        // Read the active solution name early so the create_app + create_sitemap
        // calls atomically include the new artifacts in the user's solution.
        // Don't hold this state object across the rest of the work though —
        // scaffold can run for several minutes (large table sets, slow
        // publish, role assignments) and a stale snapshot would clobber any
        // concurrent ProjectState mutation when we save at the end. We
        // re-read state fresh just before the final mutate+save.
        var earlyState = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var solutionUniqueName = string.IsNullOrWhiteSpace(earlyState.SolutionUniqueName) ? null : earlyState.SolutionUniqueName;

        // Step 2 — create the appmodule.
        // Resolve OOB icon BEFORE the create. Dataverse rejects the create
        // when webresourceid is null (the column is non-nullable), so a
        // missing icon means the create will fail with a confusing
        // "Attribute 'webresourceid' cannot be NULL" error. Plan_create_app
        // already pre-fails in this case; mirror it here so the agent gets
        // a clear "could not resolve icon" message instead of the cryptic
        // Dataverse error a few seconds later.
        Guid iconId;
        try
        {
            iconId = await dataverseClient.ResolveWebResourceIdByNameAsync(DefaultAppIconWebResourceName, cancellationToken)
                ?? Guid.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ScaffoldApp resolve_icon failed");
            result.AddFailure("resolve_icon", $"Failed to look up the OOB default icon web resource '{DefaultAppIconWebResourceName}': {ex.Message}");
            return result;
        }
        if (iconId == Guid.Empty)
        {
            result.AddFailure("resolve_icon",
                $"Could not resolve the OOB default icon web resource '{DefaultAppIconWebResourceName}' in this environment. "
                + "Dataverse rejects appmodule creates without a webresourceid, so scaffold cannot proceed. "
                + "This usually means the environment is missing core platform resources — verify with the maker portal.");
            return result;
        }

        Guid appId;
        try
        {
            var appPayload = JsonSerializer.Serialize(new
            {
                name,
                uniquename = normalizedUniqueName,
                description,
                clienttype = 4,
                navigationtype = 0,
                webresourceid = iconId.ToString(),
            });
            appId = await dataverseClient.CreateAppAsync(appPayload, solutionUniqueName, cancellationToken);
            result.AddSuccess("create_app", $"App '{name}' created with appId={appId}.");
            result.AppId = appId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ScaffoldApp create_app failed");
            result.AddFailure("create_app", ex.Message);
            return result; // hard fail — nothing else makes sense without an app
        }

        // Step 3 — build + create the sitemap.
        Guid? siteMapId = null;
        // Capture the sitemap record locally — applied to fresh state at the
        // end so we don't write a stale snapshot. Tracked WITH the
        // AttachedToAppId so teardown_app can delete the sitemap row (the
        // appmodule cascade only drops the M:N association, not the row
        // itself, so without this the next scaffold with the same
        // uniqueName would fail on duplicate sitemapnameunique).
        CreatedSiteMap? siteMapToTrack = null;
        try
        {
            var spec = BuildDefaultSitemap(normalizedUniqueName, tables);
            var validationErrors = SiteMapXmlBuilder.Validate(spec);
            if (validationErrors.Count > 0)
            {
                result.AddFailure("build_sitemap", $"Auto-generated sitemap failed validation: {string.Join("; ", validationErrors)}");
                // Continue without sitemap — the app exists but won't open until one is attached.
                // Surface clearly so the user knows the app is half-baked.
            }
            else
            {
                var xml = SiteMapXmlBuilder.Render(spec);
                var siteMapName = $"{name} Sitemap";
                var siteMapNameUnique = $"{normalizedUniqueName}sitemap";
                var siteMapPayload = JsonSerializer.Serialize(new
                {
                    sitemapname = siteMapName,
                    sitemapnameunique = siteMapNameUnique,
                    sitemapxml = xml,
                });
                siteMapId = await dataverseClient.CreateSiteMapAsync(siteMapPayload, solutionUniqueName, cancellationToken);
                result.AddSuccess("create_sitemap", $"Sitemap '{siteMapName}' created with siteMapId={siteMapId.Value}.");
                result.SiteMapId = siteMapId;
                siteMapToTrack = new CreatedSiteMap(
                    SiteMapId: siteMapId.Value,
                    SiteMapNameUnique: siteMapNameUnique,
                    AttachedToAppId: appId,
                    CreatedAt: DateTime.UtcNow);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ScaffoldApp create_sitemap failed");
            result.AddFailure("create_sitemap", ex.Message);
            // Don't return — try to attach tables anyway. The agent gets a
            // partial-success result it can act on.
        }

        // Step 4 — attach sitemap + entities. Single AddAppComponents call
        // batches the lot; we surface per-component outcomes via the merged
        // success message.
        try
        {
            var components = new List<AppComponentRef>();
            if (siteMapId is not null)
            {
                components.Add(new AppComponentRef("sitemap", siteMapId.Value));
            }
            foreach (var r in resolved)
            {
                if (r.MetadataId is not null)
                {
                    components.Add(new AppComponentRef("entity", r.MetadataId.Value));
                }
            }
            if (components.Count > 0)
            {
                var refOutcomes = await dataverseClient.AddAppComponentsAsync(appId, components, cancellationToken);
                var succeededCount = refOutcomes.Count(o => o.Succeeded);
                var failed = refOutcomes.Where(o => !o.Succeeded).ToList();
                if (failed.Count == 0)
                {
                    result.AddSuccess("attach_components",
                        $"Attached {succeededCount} component(s) (sitemap + {resolved.Length} entities).");
                }
                else
                {
                    // Quote the per-ref errors so the agent can pinpoint
                    // which component failed instead of seeing "everything
                    // failed" (the prior, misleading behavior — F-10).
                    var failedSummary = string.Join("; ",
                        failed.Select(f => $"{f.Component.Kind}/{f.Component.Id}: {f.Error}"));
                    result.AddFailure("attach_components",
                        $"Attached {succeededCount}/{components.Count} components. Failures: {failedSummary}");
                }
            }
            else
            {
                result.AddFailure("attach_components", "Nothing to attach — sitemap creation failed and no entities resolved.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ScaffoldApp attach_components failed");
            result.AddFailure("attach_components", ex.Message);
        }

        // Step 5 — publish.
        try
        {
            await dataverseClient.PublishAppAsync(appId, cancellationToken);
            result.AddSuccess("publish_app", "App published.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ScaffoldApp publish_app failed (continuing — admin can publish manually)");
            result.AddFailure("publish_app", $"Publish failed (admin can re-publish manually): {ex.Message}");
        }

        // Step 6 — role assignments. Best-effort per role; one bad role id
        // shouldn't kill the others.
        foreach (var roleId in roles)
        {
            try
            {
                await dataverseClient.AssignAppRoleAsync(appId, roleId, cancellationToken);
                result.AddSuccess($"assign_role:{roleId}", "Role assigned.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ScaffoldApp assign_role failed for {RoleId}", roleId);
                result.AddFailure($"assign_role:{roleId}", ex.Message);
            }
        }

        // Track + audit. Re-read state RIGHT before mutating so we don't
        // overwrite anything a parallel agent action wrote during the long
        // scaffold window (table-heavy scaffolds can run for minutes).
        // The reverse-op points at TeardownApp since scaffold creates
        // several artifacts that delete_app alone wouldn't fully clean up
        // (the sitemap row outlives the app).
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.Apps.Add(new CreatedApp(appId, name, normalizedUniqueName, DateTime.UtcNow));
        if (siteMapToTrack is not null)
        {
            state.SiteMaps.Add(siteMapToTrack);
        }
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.UIBuilder,
            ToolName = "ScaffoldApp",
            Parameters = new { name, uniqueName = normalizedUniqueName, tables, roles },
            // Embed the per-step result so undo/diagnostics can pin which
            // sub-step failed without re-running scaffold (e.g. publish
            // failure leaves the app in a half-built state — knowing it was
            // publish, not create, lets the user decide whether to retry
            // publish manually vs full teardown + rescaffold).
            Result = new
            {
                appId,
                siteMapId,
                overallSucceeded = result.OverallSucceeded,
                steps = result.Steps,
            },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "TeardownApp",
                Parameters = new { appId },
                Warning = "Removes the app + its auto-generated sitemap + role assignments. Tables and any other components stay.",
                RiskLevel = RiskLevel.High,
            },
        });
        state.AuditLog.Add(new AuditEntry
        {
            EntryId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            AgentName = AgentNames.UIBuilder,
            Action = "ScaffoldApp",
            Detail = $"Scaffolded app '{name}' (id={appId}) with {tables.Count} table(s) and {roles.Count} role(s).",
            Success = result.OverallSucceeded,
        });
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        sw.Stop();
        ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("tool.name", "ScaffoldApp"),
            new KeyValuePair<string, object?>("tool.status", result.OverallSucceeded ? "success" : "partial"),
            new KeyValuePair<string, object?>("scaffold.tables", tables.Count),
            new KeyValuePair<string, object?>("scaffold.roles", roles.Count));
        return result;
    }

    private async Task<(string LogicalName, Guid? MetadataId)> ResolveEntityAsync(string logicalName, CancellationToken ct)
    {
        try
        {
            var id = await dataverseClient.GetEntityMetadataIdAsync(logicalName, ct);
            return (logicalName, id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ResolveEntity failed for {LogicalName}", logicalName);
            return (logicalName, null);
        }
    }

    private static IReadOnlyList<Guid> ParseRoleIds(string? roleIdsCsv)
    {
        if (string.IsNullOrWhiteSpace(roleIdsCsv)) return Array.Empty<Guid>();
        return roleIdsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) && g != Guid.Empty ? (Guid?)g : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Default sitemap shape: one Area, one Group, one SubArea per entity.
    /// Suitable for a quick-scaffold flow; the user can call
    /// <c>plan_update_sitemap</c> later for a richer multi-area layout.
    /// </summary>
    internal static SiteMapLayoutSpec BuildDefaultSitemap(string uniqueNamePrefix, IReadOnlyList<string> tables)
    {
        // Strip publisher prefix from the area id so the SiteMap's Id is
        // user-readable in the maker portal (e.g. "gifthub_area" not
        // "eppc_gifthub_area"). Falls back to full unique name if there's no
        // underscore — safe default.
        var areaIdRoot = uniqueNamePrefix.Contains('_', StringComparison.Ordinal)
            ? uniqueNamePrefix[(uniqueNamePrefix.IndexOf('_', StringComparison.Ordinal) + 1)..]
            : uniqueNamePrefix;
        if (string.IsNullOrWhiteSpace(areaIdRoot)) areaIdRoot = "main";

        var subAreas = tables.Select(t => new SiteMapSubAreaSpec(
            Id: $"{t}_sub",
            Title: null,                   // Dataverse renders the entity's display name when Title is null
            Entity: t,
            Url: null,
            Type: null,
            PageId: null,
            Icon: null,
            Privileges: Array.Empty<SiteMapPrivilegeSpec>())).ToList();

        var group = new SiteMapGroupSpec(
            Id: $"{areaIdRoot}_group",
            Title: "Tables",
            SubAreas: subAreas);

        var area = new SiteMapAreaSpec(
            Id: $"{areaIdRoot}_area",
            Title: null,                   // Dataverse uses the app's name here
            Icon: null,
            ShowGroups: true,              // required when Groups are present
            Groups: [group]);

        return new SiteMapLayoutSpec(Areas: [area]);
    }
}

/// <summary>
/// Per-step outcome for <see cref="ScaffoldAppTool"/>. The agent uses
/// <see cref="OverallSucceeded"/> for the headline reply and
/// <see cref="Steps"/> for "what specifically failed" verbatim quotes.
/// </summary>
public sealed class ScaffoldAppResult
{
    public Guid? AppId { get; set; }
    public Guid? SiteMapId { get; set; }
    public List<ScaffoldStep> Steps { get; } = new();

    public bool OverallSucceeded => Steps.All(s => s.Succeeded);

    public void AddSuccess(string step, string detail) =>
        Steps.Add(new ScaffoldStep(step, true, detail));

    public void AddFailure(string step, string detail) =>
        Steps.Add(new ScaffoldStep(step, false, detail));
}

public sealed record ScaffoldStep(string Step, bool Succeeded, string Detail);
