using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

// AppModule component management (the `appmodulecomponent` collection that
// glues a model-driven app to its tables / forms / views / charts / dashboards
// / sitemap / business-process-flows / web resources).
//
// Carved out of DataverseClient.AppModules.cs (1,263 LOC) on 2026-05-25 to
// keep both files navigable. AppModules.cs keeps app CRUD + sitemap CRUD +
// read methods + shared helpers; this file holds Add / Remove / Get
// component flows + the per-component-kind resolution chain (the four
// Resolve*Async helpers).
//
// All members are `internal sealed partial class DataverseClient` so the
// private helpers in the sibling file are still in scope.
internal sealed partial class DataverseClient
{
    public async Task<IReadOnlyList<AppComponentRefOutcome>> AddAppComponentsAsync(
        Guid appId,
        IReadOnlyList<AppComponentRef> components,
        CancellationToken cancellationToken = default)
    {
        if (components is null || components.Count == 0)
        {
            throw new ArgumentException("AddAppComponents requires at least one component.", nameof(components));
        }

        _logger.LogDebug("AddAppComponents to {AppId}: {Count} components", appId, components.Count);

        // The Components parameter is `Collection(crmbaseentity)`. We build
        // each element to satisfy the type constraint:
        //
        //   - For real Dataverse rows (sitemap, form, view, chart, dashboard,
        //     app) → @odata.id reference, e.g. {"@odata.id":"/sitemaps(<id>)"}.
        //
        //   - For TABLES (entities) every wire-format we tried for
        //     AddAppComponents was rejected:
        //       - @odata.id /EntityDefinitions(<id>): "EntityMetadata not
        //         assignable to crmbaseentity" (EntityDefinitions returns
        //         metadata, not a base-entity row).
        //       - Inline {@odata.type:"Microsoft.Dynamics.CRM.entity"}: silent
        //         no-op (entity isn't a real concrete CRM type for this action).
        //       - POST /appmodulecomponents directly: "Create method does not
        //         support entities of type appmodulecomponent".
        //       - Inline {@odata.type:"Microsoft.Dynamics.CRM.appmodulecomponent",
        //         appmodulecomponentid:<new>, componenttype:1, objectid:<id>}:
        //         "An app can't reference the component type 'appmodulecomponent'"
        //         (intentional Dataverse validation — circular component).
        //
        //     Solution: bypass AddAppComponents for tables entirely and
        //     deep-insert via the appmodule's 1:N nav property
        //     `appmodule_appmodulecomponent`. POST against the parent's nav
        //     collection wires the FK server-side and side-steps the
        //     "appmodulecomponent has no Create message" guard because the
        //     deep-insert is treated as creation in the context of the parent.
        //     One POST per entity (chattier than the action call but reliable);
        //     non-entity kinds keep the AddAppComponents fast path that's
        //     already proven working for sitemap/form/view/chart attach.
        var outcomes = new List<AppComponentRefOutcome>(components.Count);
        var actionComponents = new List<object>(components.Count);
        var actionParticipants = new List<AppComponentRef>();
        var entityAttachments = new List<AppComponentRef>();
        foreach (var c in components)
        {
            var k = c.Kind?.ToLowerInvariant();
            if (k is "entity" or "table")
            {
                entityAttachments.Add(c);
            }
            else
            {
                try
                {
                    var entitySetPath = MapComponentRefToEntitySetPath(c.Kind ?? string.Empty);
                    actionComponents.Add(new Dictionary<string, object>
                    {
                        ["@odata.id"] = $"{entitySetPath}({c.Id})",
                    });
                    actionParticipants.Add(c);
                }
                catch (ArgumentException ex)
                {
                    // Unknown kind — record as failed and let the rest proceed
                    // so a single bad ref doesn't poison the whole batch.
                    outcomes.Add(new AppComponentRefOutcome(c, false, ex.Message));
                }
            }
        }

        // Path 1 — non-entity components via the bulk action. Atomic per
        // batch: either ALL participants succeed or ALL share the error
        // (Dataverse processes the action in one transaction). The
        // per-ref shape still gives tools the visibility they need.
        if (actionComponents.Count > 0)
        {
            var payload = new Dictionary<string, object>
            {
                ["AppId"] = appId,
                ["Components"] = actionComponents,
            };
            var addJson = JsonSerializer.Serialize(payload);
            var addContent = new StringContent(addJson, Encoding.UTF8, "application/json");
            try
            {
                var addResponse = await _http.PostAsync($"{ApiBase}AddAppComponents", addContent, cancellationToken);
                await EnsureSuccessOrSurfaceAsync(addResponse,
                    $"AddAppComponents ({actionComponents.Count} non-entity components) to app ({appId})",
                    cancellationToken);
                foreach (var c in actionParticipants)
                {
                    outcomes.Add(new AppComponentRefOutcome(c, true, null));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "AddAppComponents bulk action failed for {AppId}", appId);
                foreach (var c in actionParticipants)
                {
                    outcomes.Add(new AppComponentRefOutcome(c, false, ex.Message));
                }
            }
        }

        // Path 2 — tables via deep-insert per entity. Independent per row;
        // entity 3 failing doesn't poison entities 1, 2, 4, 5.
        foreach (var entity in entityAttachments)
        {
            try
            {
                var body = new Dictionary<string, object>
                {
                    ["componenttype"] = AppModuleComponentType.Entity,
                    ["objectid"] = entity.Id,
                    ["rootcomponentbehavior"] = 0,
                };
                var bodyJson = JsonSerializer.Serialize(body);
                var bodyContent = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                var url = $"{ApiBase}appmodules({appId})/appmodule_appmodulecomponent";
                var response = await _http.PostAsync(url, bodyContent, cancellationToken);
                await EnsureSuccessOrSurfaceAsync(response,
                    $"Attach entity {entity.Id} to app ({appId}) via nav property",
                    cancellationToken);
                outcomes.Add(new AppComponentRefOutcome(entity, true, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "AddAppComponents per-entity attach failed for {EntityId} on app {AppId}",
                    entity.Id, appId);
                outcomes.Add(new AppComponentRefOutcome(entity, false, ex.Message));
            }
        }

        return outcomes.AsReadOnly();
    }

    public async Task<IReadOnlyList<AppComponentRefOutcome>> RemoveAppComponentsAsync(
        Guid appId,
        IReadOnlyList<AppComponentRef> components,
        CancellationToken cancellationToken = default)
    {
        if (components is null || components.Count == 0)
        {
            throw new ArgumentException("RemoveAppComponents requires at least one component.", nameof(components));
        }

        _logger.LogDebug("RemoveAppComponents from {AppId}: {Count} components", appId, components.Count);

        // Symmetric with AddAppComponents — non-entity components via the
        // RemoveAppComponents action; entities via direct DELETE of the
        // appmodulecomponent join row, located by (appId, objectid).
        var outcomes = new List<AppComponentRefOutcome>(components.Count);
        var actionComponents = new List<object>(components.Count);
        var actionParticipants = new List<AppComponentRef>();
        var entityDetachments = new List<AppComponentRef>();
        foreach (var c in components)
        {
            var k = c.Kind?.ToLowerInvariant();
            if (k is "entity" or "table")
            {
                entityDetachments.Add(c);
            }
            else
            {
                try
                {
                    var entitySetPath = MapComponentRefToEntitySetPath(c.Kind ?? string.Empty);
                    actionComponents.Add(new Dictionary<string, object>
                    {
                        ["@odata.id"] = $"{entitySetPath}({c.Id})",
                    });
                    actionParticipants.Add(c);
                }
                catch (ArgumentException ex)
                {
                    outcomes.Add(new AppComponentRefOutcome(c, false, ex.Message));
                }
            }
        }

        if (actionComponents.Count > 0)
        {
            var payload = new Dictionary<string, object>
            {
                ["AppId"] = appId,
                ["Components"] = actionComponents,
            };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"{ApiBase}RemoveAppComponents";
            try
            {
                var response = await _http.PostAsync(url, content, cancellationToken);
                await EnsureSuccessOrSurfaceAsync(response,
                    $"RemoveAppComponents ({actionComponents.Count} non-entity components) from app ({appId})",
                    cancellationToken);
                foreach (var c in actionParticipants)
                {
                    outcomes.Add(new AppComponentRefOutcome(c, true, null));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "RemoveAppComponents bulk action failed for {AppId}", appId);
                foreach (var c in actionParticipants)
                {
                    outcomes.Add(new AppComponentRefOutcome(c, false, ex.Message));
                }
            }
        }

        foreach (var entity in entityDetachments)
        {
            // Look up the join row PK for this (app, entity) pair, then DELETE
            // it. Direct DELETE is safe because the join row carries no
            // referential integrity beyond the FK to the app and the metadata
            // id we already know.
            try
            {
                var lookupUrl = $"{ApiBase}appmodulecomponents"
                    + $"?$select=appmodulecomponentid"
                    + $"&$filter=_appmoduleidunique_value eq {appId} and objectid eq {entity.Id} and componenttype eq {AppModuleComponentType.Entity}"
                    + $"&$top=1";
                var lookupResponse = await _http.GetAsync(lookupUrl, cancellationToken);
                await EnsureSuccessOrSurfaceAsync(lookupResponse,
                    $"Locate appmodulecomponent join row for entity {entity.Id}",
                    cancellationToken);
                var lookup = await lookupResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                if (!lookup.TryGetProperty("value", out var rows) || rows.GetArrayLength() == 0)
                {
                    _logger.LogDebug("No appmodulecomponent join row for entity {EntityId} on app {AppId} — already detached",
                        entity.Id, appId);
                    // Idempotent — treat "already detached" as success so the
                    // tool doesn't surface a misleading "failed" outcome.
                    outcomes.Add(new AppComponentRefOutcome(entity, true, "Already detached (no join row)."));
                    continue;
                }
                var joinId = rows[0].GetProperty("appmodulecomponentid").GetGuid();
                var deleteResponse = await _http.DeleteAsync(
                    $"{ApiBase}appmodulecomponents({joinId})", cancellationToken);
                await EnsureSuccessOrSurfaceAsync(deleteResponse,
                    $"Delete appmodulecomponent {joinId} (entity {entity.Id} from app {appId})",
                    cancellationToken);
                outcomes.Add(new AppComponentRefOutcome(entity, true, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "RemoveAppComponents per-entity detach failed for {EntityId} on app {AppId}",
                    entity.Id, appId);
                outcomes.Add(new AppComponentRefOutcome(entity, false, ex.Message));
            }
        }

        return outcomes.AsReadOnly();
    }

    public async Task<Guid?> GetEntityMetadataIdAsync(
        string tableLogicalName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tableLogicalName)) return null;
        var normalized = tableLogicalName.Trim().ToLowerInvariant();

        _logger.LogDebug("Resolving entity MetadataId for {LogicalName}", normalized);

        // Dataverse logical names are URL-safe (lowercase alphanumeric +
        // underscore). The single-quote inside the parens follows the OData
        // convention for string-keyed alternate-key lookups on metadata.
        var url = $"{ApiBase}EntityDefinitions(LogicalName='{normalized}')?$select=MetadataId";

        var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSurfaceAsync(response, $"GetEntityMetadataId ({normalized})", cancellationToken);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (doc.ValueKind != JsonValueKind.Object) return null;
        if (!doc.TryGetProperty("MetadataId", out var idProp)) return null;
        if (idProp.ValueKind != JsonValueKind.String) return null;
        return Guid.TryParse(idProp.GetString(), out var g) ? g : null;
    }

    /// <summary>
    /// Maps a friendly component kind to the Dataverse OData entity-set
    /// path used in the <c>@odata.id</c> reference form for
    /// AddAppComponents / RemoveAppComponents. The path is relative
    /// (e.g. <c>/sitemaps</c>) and the caller appends <c>(&lt;guid&gt;)</c>.
    ///
    /// The entity case is special — tables are metadata, not a regular
    /// table — so they reference <c>/EntityDefinitions(&lt;metadataid&gt;)</c>,
    /// not an entity-set. Caller must pre-resolve the table's MetadataId
    /// (see <see cref="GetEntityMetadataIdAsync"/>).
    /// </summary>
    private static string MapComponentRefToEntitySetPath(string kind) => kind.ToLowerInvariant() switch
    {
        "sitemap" => "/sitemaps",
        "systemform" or "form" => "/systemforms",
        // Dashboards live in the systemform table (with type=0). Same path.
        "dashboard" => "/systemforms",
        "savedquery" or "view" => "/savedqueries",
        "savedqueryvisualization" or "chart" => "/savedqueryvisualizations",
        "appmodule" or "app" => "/appmodules",
        "entity" or "table" => "/EntityDefinitions",
        _ => throw new ArgumentException(
            $"Unknown component kind '{kind}'. Supported: sitemap, systemform, form, dashboard, savedquery, view, savedqueryvisualization, chart, appmodule, app, entity, table.",
            nameof(kind)),
    };

    private static Guid? TryGetGuid(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var p) &&
                p.ValueKind == JsonValueKind.String &&
                Guid.TryParse(p.GetString(), out var g))
            {
                return g;
            }
        }
        return null;
    }

    private static int? TryGetInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var p) &&
                p.ValueKind == JsonValueKind.Number &&
                p.TryGetInt32(out var n))
            {
                return n;
            }
        }
        return null;
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Lists every component attached to an app module by merging two storage
    /// paths — Dataverse uses both depending on how the app was created:
    ///
    ///   1. The <c>appmodulecomponent</c> JOIN TABLE — populated when an app
    ///      is built via the Web API deep-insert path (our Phase 3 attach).
    ///      Reliable for apps we created; may be empty for maker-portal apps.
    ///   2. The <c>descriptor</c> JSON column on <c>appmodule</c> — populated
    ///      when the app is published, especially for maker-portal apps.
    ///      <c>appInfo.AppComponents.Entities[]</c> carries entity LogicalName
    ///      directly; <c>appInfo.Components[]</c> carries every component's
    ///      Id + Type. Real session 2026-05-06: the "Test" app had its 3
    ///      entities ONLY in the descriptor — the join table was empty.
    ///
    /// We previously called the unbound <c>RetrieveAppComponents</c> action,
    /// but a real session confirmed that endpoint silently omits entity-
    /// component rows in some Dataverse environments. Direct read of both
    /// authoritative sources is more reliable.
    ///
    /// Sources are merged by (componenttype, objectid). Descriptor wins for
    /// name resolution since it carries LogicalName for entities natively
    /// — saves an EntityDefinitions round-trip per entity. Components only
    /// the join table knows about (or only the descriptor knows about) are
    /// kept either way. Duplicates collapse cleanly.
    /// </summary>
    public async Task<IReadOnlyList<AppModuleComponentRef>> GetAppComponentsAsync(
        Guid appId,
        string? descriptorJson = null,
        CancellationToken cancellationToken = default)
    {
        // Source A: descriptor parse. If the caller didn't pre-supply one
        // (most standalone callers don't), fetch the app to get it. Cheap
        // — descriptor is on the app row itself, no extra round-trip vs.
        // any read tool the agent would call anyway.
        if (descriptorJson is null)
        {
            var live = await GetAppAsync(appId, cancellationToken);
            descriptorJson = live?.Descriptor;
        }
        var descriptorRows = ParseDescriptorComponents(descriptorJson);

        // Source B: appmodulecomponent join-table query.
        var joinUrl = $"{ApiBase}appmodulecomponents"
            + "?$select=appmodulecomponentid,componenttype,objectid"
            + $"&$filter=_appmoduleidunique_value eq {appId}";
        var joinRows = new List<(int ComponentType, Guid ObjectId)>();
        var joinResponse = await _http.GetAsync(joinUrl, cancellationToken);
        if (joinResponse.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessOrSurfaceAsync(joinResponse,
                $"List appmodulecomponents for app ({appId})", cancellationToken);
            var root = await joinResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in arr.EnumerateArray())
                {
                    var ct = GetIntProp(element, "componenttype");
                    var oid = GetGuidProp(element, "objectid");
                    if (oid != Guid.Empty)
                    {
                        joinRows.Add((ct, oid));
                    }
                }
            }
        }

        // Merge by (Type, Id). Descriptor wins for LogicalName; if both
        // sources mention the same row but only the join table knows it,
        // we still resolve via the appropriate metadata/row endpoint.
        var merged = new Dictionary<(int Type, Guid Id), (string? LogicalName, string? Name)>();
        foreach (var row in descriptorRows)
        {
            merged[(row.Type, row.Id)] = (row.LogicalName, null);
        }
        foreach (var row in joinRows)
        {
            merged.TryAdd((row.ComponentType, row.ObjectId), (null, null));
        }
        if (merged.Count == 0) return Array.Empty<AppModuleComponentRef>();

        // Resolve each merged row in parallel. Skip the EntityDefinitions
        // call entirely when the descriptor already gave us the LogicalName.
        var resolveTasks = merged.Select(kv =>
            ResolveOrShortcircuitAsync(kv.Key.Type, kv.Key.Id, kv.Value.LogicalName, cancellationToken));
        var resolved = await Task.WhenAll(resolveTasks);
        return resolved.Where(c => c is not null).Select(c => c!).ToList().AsReadOnly();
    }

    /// <summary>
    /// If the descriptor already supplied a LogicalName for an entity row,
    /// emit the ref directly without hitting EntityDefinitions. Otherwise
    /// fall through to the same per-type resolver as before.
    /// </summary>
    private async Task<AppModuleComponentRef?> ResolveOrShortcircuitAsync(
        int componentType, Guid objectId, string? descriptorLogicalName,
        CancellationToken cancellationToken)
    {
        if (componentType == 1 && !string.IsNullOrWhiteSpace(descriptorLogicalName))
        {
            // Descriptor's LogicalName is canonical (e.g. "account",
            // "eppc_gift"). Use it as both Name and LogicalName until/unless
            // a caller specifically wants the localized DisplayName — at
            // which point we'd add a separate "rich resolve" path.
            return new AppModuleComponentRef("entity", objectId, descriptorLogicalName, descriptorLogicalName);
        }
        return await ResolveAppComponentAsync(componentType, objectId, cancellationToken);
    }

    /// <summary>
    /// Parse an app's <c>descriptor</c> JSON for component rows.
    /// Looks at both <c>appInfo.Components[]</c> (id + type for everything)
    /// and <c>appInfo.AppComponents.Entities[]</c> (id + LogicalName for
    /// entities specifically — richer than the Components entry).
    /// Returns an empty list on null/malformed input.
    /// </summary>
    internal static List<(int Type, Guid Id, string? LogicalName)> ParseDescriptorComponents(string? descriptorJson)
    {
        var result = new List<(int, Guid, string?)>();
        if (string.IsNullOrWhiteSpace(descriptorJson)) return result;

        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(descriptorJson);
        }
        catch (JsonException)
        {
            return result;
        }
        if (root.ValueKind != JsonValueKind.Object) return result;
        if (!root.TryGetProperty("appInfo", out var appInfo) || appInfo.ValueKind != JsonValueKind.Object)
            return result;

        // Index entity LogicalNames first so we can attach them to the
        // matching Components[] entries (which carry the Type).
        var entityNames = new Dictionary<Guid, string>();
        if (appInfo.TryGetProperty("AppComponents", out var appComp) && appComp.ValueKind == JsonValueKind.Object
            && appComp.TryGetProperty("Entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in entities.EnumerateArray())
            {
                var id = GetGuidProp(e, "Id");
                var ln = GetStringProp(e, "LogicalName");
                if (id != Guid.Empty && !string.IsNullOrWhiteSpace(ln))
                {
                    entityNames[id] = ln!;
                }
            }
        }

        if (appInfo.TryGetProperty("Components", out var components) && components.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in components.EnumerateArray())
            {
                var id = GetGuidProp(c, "Id");
                if (id == Guid.Empty) continue;
                var type = GetIntProp(c, "Type");
                entityNames.TryGetValue(id, out var logicalName);
                result.Add((type, id, logicalName));
            }
        }

        // Some descriptor variants only emit AppComponents.Entities and
        // omit them from the top-level Components array. Make sure those
        // still appear as entity-type rows.
        foreach (var (id, ln) in entityNames)
        {
            if (!result.Any(r => r.Item2 == id))
            {
                result.Add((AppModuleComponentType.Entity, id, ln));
            }
        }

        return result;
    }

    /// <summary>
    /// Resolve a single (componenttype, objectid) pair into a typed
    /// <see cref="AppModuleComponentRef"/> by hitting the appropriate read
    /// endpoint. Best-effort: if the row was deleted out from under us
    /// (e.g. an orphan join row) we log + still emit a bare ref so the
    /// agent's "what's attached" answer doesn't silently drop it.
    /// </summary>
    private async Task<AppModuleComponentRef?> ResolveAppComponentAsync(
        int componentType, Guid objectId, CancellationToken cancellationToken)
    {
        try
        {
            return componentType switch
            {
                AppModuleComponentType.Entity => await ResolveEntityComponentAsync(objectId, cancellationToken),
                AppModuleComponentType.SavedQuery => await ResolveSimpleComponentAsync("savedqueries", objectId,
                    "savedqueryid", "name", "returnedtypecode", "savedquery", cancellationToken),
                AppModuleComponentType.BusinessProcessFlow => await ResolveSimpleComponentAsync("workflows", objectId,
                    "workflowid", "name", "primaryentity", "businessprocessflow", cancellationToken),
                AppModuleComponentType.SavedQueryVisualization => await ResolveSimpleComponentAsync("savedqueryvisualizations", objectId,
                    "savedqueryvisualizationid", "name", "primaryentitytypecode", "savedqueryvisualization", cancellationToken),
                AppModuleComponentType.SystemForm => await ResolveSimpleComponentAsync("systemforms", objectId,
                    "formid", "name", "objecttypecode", "systemform", cancellationToken),
                AppModuleComponentType.SiteMap => await ResolveSimpleComponentAsync("sitemaps", objectId,
                    "sitemapid", "sitemapname", null, "sitemap", cancellationToken),
                _ => new AppModuleComponentRef($"componenttype-{componentType}", objectId, null, null),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to resolve appmodulecomponent (type={ComponentType}, objectid={ObjectId}); returning bare ref",
                componentType, objectId);
            return new AppModuleComponentRef(MapComponentTypeToKind(componentType), objectId, null, null);
        }
    }

    private async Task<AppModuleComponentRef?> ResolveEntityComponentAsync(
        Guid metadataId, CancellationToken cancellationToken)
    {
        // MetadataId IS the primary key for EntityDefinitions — use the
        // primary-key syntax `EntityDefinitions(<guid>)`, NOT the
        // alternate-key syntax `EntityDefinitions(MetadataId=<guid>)`. The
        // latter quietly returned 404 in real session 2026-05-06, leaving
        // entity-component rows with no resolved name. The
        // `LogicalName='X'` alternate-key form is the only alternate-key
        // syntax this entity set supports — for id lookups, primary-key
        // form is canonical.
        var url = $"{ApiBase}EntityDefinitions({metadataId})?$select=LogicalName,DisplayName";
        var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new AppModuleComponentRef("entity", metadataId, null, null);
        await EnsureSuccessOrSurfaceAsync(response, $"Resolve entity metadata ({metadataId})", cancellationToken);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var logicalName = GetStringProp(doc, "LogicalName");
        var displayName = ExtractLocalizedDisplayName(doc);
        // Prefer LogicalName as the surfaced "name" for entity rows when no
        // localized DisplayName is available — same convention the descriptor
        // path uses, keeps the agent's "what's attached" answer consistent
        // regardless of which source supplied the row.
        return new AppModuleComponentRef("entity", metadataId, displayName ?? logicalName, logicalName);
    }

    private async Task<AppModuleComponentRef?> ResolveSimpleComponentAsync(
        string entitySet, Guid id,
        string idField, string nameField, string? logicalNameField,
        string kind, CancellationToken cancellationToken)
    {
        var select = logicalNameField is null
            ? $"{idField},{nameField}"
            : $"{idField},{nameField},{logicalNameField}";
        var url = $"{ApiBase}{entitySet}({id})?$select={select}";
        var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new AppModuleComponentRef(kind, id, null, null);
        await EnsureSuccessOrSurfaceAsync(response, $"Resolve {kind} ({id})", cancellationToken);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var name = GetStringProp(doc, nameField);
        var logicalName = logicalNameField is null ? null : GetStringProp(doc, logicalNameField);
        return new AppModuleComponentRef(kind, id, name, logicalName);
    }

    /// <summary>
    /// Legacy helper retained for <c>AppModuleComponentMappingTests</c>: the
    /// branching logic for the heterogeneous <c>RetrieveAppComponents</c>
    /// response shape. No production path calls this now that
    /// <see cref="GetAppComponentsAsync"/> queries the join table directly.
    /// </summary>
    internal static (string Kind, Guid Id, string? Name, string? LogicalName) MapAppComponent(JsonElement element)
    {
        var typeName = element.TryGetProperty("@odata.type", out var t) ? t.GetString() ?? string.Empty : string.Empty;

        // Strip the "#Microsoft.Dynamics.CRM." prefix if present.
        var bareType = typeName.StartsWith("#Microsoft.Dynamics.CRM.", StringComparison.OrdinalIgnoreCase)
            ? typeName["#Microsoft.Dynamics.CRM.".Length..]
            : typeName.TrimStart('#');

        return bareType.ToLowerInvariant() switch
        {
            "entity" or "entitymetadata" or "entitymetadatabase" =>
                ("entity",
                 GetGuidProp(element, "MetadataId"),
                 ExtractLocalizedDisplayName(element),
                 GetStringProp(element, "LogicalName")),

            "systemform" =>
                ("systemform",
                 GetGuidProp(element, "formid"),
                 GetStringProp(element, "name"),
                 GetStringProp(element, "objecttypecode")),

            "savedquery" =>
                ("savedquery",
                 GetGuidProp(element, "savedqueryid"),
                 GetStringProp(element, "name"),
                 GetStringProp(element, "returnedtypecode")),

            "savedqueryvisualization" =>
                ("savedqueryvisualization",
                 GetGuidProp(element, "savedqueryvisualizationid"),
                 GetStringProp(element, "name"),
                 GetStringProp(element, "primaryentitytypecode")),

            "sitemap" =>
                ("sitemap",
                 GetGuidProp(element, "sitemapid"),
                 GetStringProp(element, "sitemapnameunique") ?? GetStringProp(element, "sitemapname"),
                 null),

            // RetrieveAppComponents on current Dataverse environments returns
            // appmodulecomponent JOIN-TABLE rows (not the polymorphic union
            // of EntityMetadata + SystemForm + SavedQuery + ... that earlier
            // docs implied). Each row carries `objectid` (the GUID of the
            // referenced component) and `componenttype` (the SolutionComponentType
            // int — Entity=1, SystemForm=60, SavedQuery=26, SiteMap=62, …).
            // Surface objectid as the canonical id so downstream consumers can
            // resolve the actual entity/form/sitemap, and map componenttype to a
            // friendly kind name.
            "appmodulecomponent" =>
                (MapComponentTypeToKind(GetIntProp(element, "componenttype")),
                 GetGuidProp(element, "objectid"),
                 GetStringProp(element, "appmodulecomponentdescription"),
                 null),

            "appmodule" =>
                ("appmodule",
                 GetGuidProp(element, "appmoduleid"),
                 GetStringProp(element, "name"),
                 GetStringProp(element, "uniquename")),

            "workflow" =>
                ("workflow",
                 GetGuidProp(element, "workflowid"),
                 GetStringProp(element, "name"),
                 GetStringProp(element, "primaryentity")),

            _ =>
                (string.IsNullOrEmpty(bareType) ? "unknown" : bareType.ToLowerInvariant(),
                 GuessIdFromAnyGuidProperty(element),
                 GetStringProp(element, "name"),
                 null),
        };
    }
}
