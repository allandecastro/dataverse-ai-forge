using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed partial class DataverseClient
{
    // Model-driven apps (appmodule entity). Phase 1 ships read-only methods —
    // list / details / full / sitemap-for-app / roles-for-app. Writes land in
    // later phases.
    //
    // Web API references:
    //   appmodule:    https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/appmodule
    //   sitemap:      https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/sitemap
    //   appmoduleroles_association: M:N to "role" — exposed as a collection-valued
    //                 navigation property on appmodule.
    //   appmodule_sitemap:         M:N to "sitemap" — same shape.
    //   RetrieveAppComponents:     bound function on appmodule that returns the
    //                              union of component metadata records currently
    //                              attached to the app. Heterogeneous response
    //                              branched by `@odata.type`.

    // Conservative $select column list. The webresource lookup is omitted from
    // the list view to avoid environment-specific 400s on `_webresourceid_value`
    // in some Dataverse releases — agents that need the welcome-page web
    // resource can call get_app_details for a single app instead.
    private const string AppModuleSelect =
        "appmoduleid,name,uniquename,description,"
        + "clienttype,navigationtype,statecode,statuscode,ismanaged,publishedon";

    // Drop _webresourceid_value here too — same environment-specific issue that
    // broke the list call. descriptor + configxml are the load-bearing extras
    // for a single-app read; the welcome page lookup isn't worth the 400 risk.
    private const string AppModuleDetailsSelect = AppModuleSelect + ",descriptor,configxml";

    public async Task<IReadOnlyList<AppModuleInfo>> GetAppsListAsync(
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying appmodule list (includeUnpublished={IncludeUnpublished})", includeUnpublished);

        var url = $"{ApiBase}appmodules?$select={AppModuleSelect}";
        if (!includeUnpublished)
        {
            // statecode=0 → Active. Apps with statecode=1 are deactivated and
            // wouldn't appear in the user's app picker — exclude them by
            // default, mirroring what the maker portal shows.
            url += "&$filter=statecode eq 0";
        }

        // Use GetAsync + EnsureSuccessOrSurfaceAsync so any 4xx surfaces the actual
        // OData error body to the agent instead of a generic "Function failed".
        var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "List model-driven apps (appmodules)", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<ODataResponse<AppModuleResponse>>(cancellationToken)
            ?? throw new InvalidOperationException("appmodule list returned null");

        return payload.Value.Select(MapInfo).ToList().AsReadOnly();
    }

    public async Task<AppModuleDetails?> GetAppAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reading appmodule {AppId}", appId);

        var url = $"{ApiBase}appmodules({appId})?$select={AppModuleDetailsSelect}";

        var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSurfaceAsync(response, $"Read app ({appId})", cancellationToken);

        var app = await response.Content.ReadFromJsonAsync<AppModuleResponse>(cancellationToken);
        return app is null ? null : MapDetails(app);
    }

    public async Task<AppModuleFullDetails?> GetAppFullAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reading full appmodule {AppId} (app + components + sitemap + roles)", appId);

        // Fan-out: 4 parallel reads. The app itself is required (returns null if
        // missing); the other 3 are best-effort — a partial failure (e.g. an
        // environment that returns 403 on RetrieveAppComponents for a managed
        // system app) shouldn't kill the whole answer. Log + degrade per branch
        // so the agent gets whatever the user asked for.
        var appTask = GetAppAsync(appId, cancellationToken);
        var componentsTask = SafeReadAsync(
            () => GetAppComponentsAsync(appId, cancellationToken),
            (IReadOnlyList<AppModuleComponentRef>)Array.Empty<AppModuleComponentRef>(),
            $"RetrieveAppComponents AppId={appId}");
        var sitemapTask = SafeReadAsync<SiteMapInfo?>(
            () => GetSiteMapForAppAsync(appId, cancellationToken),
            null,
            $"GetSiteMapForApp AppId={appId}");
        var rolesTask = SafeReadAsync(
            () => ListAppRolesAsync(appId, cancellationToken),
            (IReadOnlyList<AppModuleRoleInfo>)Array.Empty<AppModuleRoleInfo>(),
            $"ListAppRoles AppId={appId}");

        await Task.WhenAll(appTask, componentsTask, sitemapTask, rolesTask);

        var app = appTask.Result;
        if (app is null) return null;

        return new AppModuleFullDetails(
            app,
            componentsTask.Result,
            sitemapTask.Result,
            rolesTask.Result);
    }

    /// <summary>
    /// Wraps a sub-read so a single failed branch in <see cref="GetAppFullAsync"/>
    /// degrades to a default value instead of killing the whole composite read.
    /// Logs the exception so the failure remains diagnosable from the backend log.
    /// </summary>
    private async Task<T> SafeReadAsync<T>(Func<Task<T>> operation, T fallback, string label)
    {
        try { return await operation(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetAppFull sub-read failed: {Label}", label);
            return fallback;
        }
    }

    public async Task<SiteMapInfo?> GetSiteMapForAppAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reading sitemap for appmodule {AppId}", appId);

        // appmodule_sitemap is the N:N collection-valued navigation property
        // from appmodule to sitemap. An app has 0 or 1 sitemap in practice,
        // but the relationship is N:N so we take the first match.
        var url = $"{ApiBase}appmodules({appId})/appmodule_sitemap"
            + "?$select=sitemapid,sitemapname,sitemapnameunique,sitemapxml,ismanaged";

        var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSurfaceAsync(response, $"Read sitemap for app ({appId})", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<ODataResponse<SiteMapResponse>>(cancellationToken);
        var first = payload?.Value.FirstOrDefault();
        return first is null ? null : MapSiteMap(first);
    }

    public async Task<IReadOnlyList<AppModuleRoleInfo>> ListAppRolesAsync(
        Guid appId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing roles for appmodule {AppId}", appId);

        // appmoduleroles_association is the canonical M:N navigation between
        // appmodule and role on Web API.
        var url = $"{ApiBase}appmodules({appId})/appmoduleroles_association"
            + "?$select=roleid,name,_businessunitid_value,ismanaged";

        var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return Array.Empty<AppModuleRoleInfo>();
        await EnsureSuccessOrSurfaceAsync(response, $"List roles for app ({appId})", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<ODataResponse<RoleResponse>>(cancellationToken);
        if (payload is null) return Array.Empty<AppModuleRoleInfo>();

        return payload.Value
            .Select(r => new AppModuleRoleInfo(
                r.RoleId,
                r.Name ?? string.Empty,
                string.IsNullOrWhiteSpace(r.BusinessUnitId) ? null : Guid.Parse(r.BusinessUnitId!),
                r.IsManaged ?? false))
            .ToList()
            .AsReadOnly();
    }

    // ── webresource resolution (used by Phase 2 app create) ────────────

    public async Task<Guid?> ResolveWebResourceIdByNameAsync(
        string webResourceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webResourceName)) return null;

        _logger.LogDebug("Resolving webresource by name: {Name}", webResourceName);

        // OData $filter requires the value wrapped in single quotes; we already
        // assume the caller doesn't pass a name with embedded quotes (web
        // resource names are constrained: alphanumeric + slash + underscore +
        // dot + hyphen, no quotes).
        var filter = Uri.EscapeDataString($"name eq '{webResourceName}'");
        var url = $"{ApiBase}webresourceset?$select=webresourceid,name&$filter={filter}&$top=1";

        var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSurfaceAsync(response, $"Resolve webresource by name '{webResourceName}'", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<ODataResponse<WebResourceLookupResponse>>(cancellationToken);
        var first = payload?.Value.FirstOrDefault();
        return first?.WebResourceId;
    }

    private sealed class WebResourceLookupResponse
    {
        [JsonPropertyName("webresourceid")]
        public Guid WebResourceId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    // ── writes (Phase 2) ────────────────────────────────────────────────

    public async Task<Guid> CreateAppAsync(
        string webApiPayloadJson,
        string? solutionUniqueName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating appmodule, payload={Length} chars (solution={Solution})",
            webApiPayloadJson.Length, solutionUniqueName ?? "<none>");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}appmodules") { Content = content };
        // MSCRM.SolutionUniqueName makes Dataverse add the new component to the
        // named solution atomically. Cleaner than the post-create
        // AddSolutionComponent dance the dashboard path uses (no
        // eventual-consistency retry needed).
        if (!string.IsNullOrWhiteSpace(solutionUniqueName))
        {
            request.Headers.Add("MSCRM.SolutionUniqueName", solutionUniqueName);
        }
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create app (appmodule)", cancellationToken);
        return ExtractMetadataIdFromHeader(response);
    }

    public async Task UpdateAppAsync(Guid appId, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating appmodule {AppId}, payload={Length} chars", appId, webApiPayloadJson.Length);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{ApiBase}appmodules({appId})") { Content = content };
        // If-Match: * forces Dataverse to apply only if the row exists.
        // Without it, PATCH on a missing id silently upserts — which is the
        // opposite of what an "update" tool should do.
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Update app ({appId})", cancellationToken);
    }

    public async Task DeleteAppAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting appmodule {AppId}", appId);

        var response = await _http.DeleteAsync($"{ApiBase}appmodules({appId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Delete app ({appId})", cancellationToken);
    }

    public async Task<AppValidationResult> ValidateAppAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Validating appmodule {AppId}", appId);

        // Like AddAppComponents, ValidateApp is an UNBOUND action — the bound
        // URL `/appmodules({id})/Microsoft.Dynamics.CRM.ValidateApp` returns
        // 404 "Resource not found". The SDK class `ValidateAppRequest` has
        // `AppModuleId` as the property name, so we ship that in the body.
        // Returns a JSON envelope with a collection of validation issues; we
        // surface them verbatim so the agent can quote the actual reason
        // ("App is missing a sitemap", etc.).
        var payload = new { AppModuleId = appId };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}ValidateApp", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"ValidateApp ({appId})", cancellationToken);

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Real Dataverse responses for ValidateApp have varied across versions:
        // sometimes the issue list is at the top level under "ValidationIssueList",
        // sometimes under "value", sometimes under "Result". Try the documented
        // shape first, then fall back to the others.
        JsonElement issuesArr = default;
        var issuesFound = false;
        foreach (var key in new[] { "ValidationIssueList", "Result", "value", "ValidationIssues" })
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(key, out var arr)
                && arr.ValueKind == JsonValueKind.Array)
            {
                issuesArr = arr;
                issuesFound = true;
                break;
            }
        }

        var issues = new List<AppValidationIssue>();
        if (issuesFound)
        {
            foreach (var element in issuesArr.EnumerateArray())
            {
                issues.Add(new AppValidationIssue(
                    Severity: GetStringProp(element, "Severity") ?? GetStringProp(element, "severity") ?? "Error",
                    Message: GetStringProp(element, "Message") ?? GetStringProp(element, "message") ?? string.Empty,
                    ComponentName: GetStringProp(element, "ComponentName") ?? GetStringProp(element, "componentName"),
                    ComponentId: TryGetGuid(element, "ComponentId", "componentId"),
                    ComponentType: TryGetInt(element, "ComponentType", "componentType")));
            }
        }

        var hasErrors = issues.Any(i => string.Equals(i.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        return new AppValidationResult(appId, !hasErrors, issues);
    }

    public async Task PublishAppAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Publishing app {AppId}", appId);

        // PublishXml accepts a parameterXml string scoping the publish. For an
        // app, we pass an <importexportxml><appmodules><appmodule>{guid}</appmodule>
        // </appmodules></importexportxml> envelope so we don't republish the
        // entire org.
        var parameterXml =
            "<importexportxml>"
                + "<appmodules>"
                    + $"<appmodule>{appId}</appmodule>"
                + "</appmodules>"
            + "</importexportxml>";

        var payload = new { ParameterXml = parameterXml };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}PublishXml", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"PublishApp ({appId})", cancellationToken);
    }

    // ── SiteMap writes (Phase 4) ────────────────────────────────────────

    public async Task<Guid> CreateSiteMapAsync(
        string webApiPayloadJson,
        string? solutionUniqueName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating sitemap, payload={Length} chars (solution={Solution})",
            webApiPayloadJson.Length, solutionUniqueName ?? "<none>");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}sitemaps") { Content = content };
        if (!string.IsNullOrWhiteSpace(solutionUniqueName))
        {
            request.Headers.Add("MSCRM.SolutionUniqueName", solutionUniqueName);
        }
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create sitemap", cancellationToken);
        return ExtractMetadataIdFromHeader(response);
    }

    public async Task UpdateSiteMapAsync(Guid siteMapId, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating sitemap {SiteMapId}, payload={Length} chars", siteMapId, webApiPayloadJson.Length);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{ApiBase}sitemaps({siteMapId})") { Content = content };
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Update sitemap ({siteMapId})", cancellationToken);
    }

    public async Task AddAppComponentsAsync(
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
        var actionComponents = new List<object>(components.Count);
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
                var entitySetPath = MapComponentRefToEntitySetPath(c.Kind ?? string.Empty);
                actionComponents.Add(new Dictionary<string, object>
                {
                    ["@odata.id"] = $"{entitySetPath}({c.Id})",
                });
            }
        }

        // Path 1 — non-entity components via the bulk action.
        if (actionComponents.Count > 0)
        {
            var payload = new Dictionary<string, object>
            {
                ["AppId"] = appId,
                ["Components"] = actionComponents,
            };
            var addJson = JsonSerializer.Serialize(payload);
            var addContent = new StringContent(addJson, Encoding.UTF8, "application/json");
            var addResponse = await _http.PostAsync($"{ApiBase}AddAppComponents", addContent, cancellationToken);
            await EnsureSuccessOrSurfaceAsync(addResponse,
                $"AddAppComponents ({actionComponents.Count} non-entity components) to app ({appId})",
                cancellationToken);
        }

        // Path 2 — tables via deep-insert per entity. The action handler
        // refuses; the nav-property POST does not.
        foreach (var entity in entityAttachments)
        {
            var body = new Dictionary<string, object>
            {
                ["componenttype"] = 1,
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
        }
    }

    public async Task RemoveAppComponentsAsync(
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
        var actionComponents = new List<object>(components.Count);
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
                var entitySetPath = MapComponentRefToEntitySetPath(c.Kind ?? string.Empty);
                actionComponents.Add(new Dictionary<string, object>
                {
                    ["@odata.id"] = $"{entitySetPath}({c.Id})",
                });
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
            var response = await _http.PostAsync(url, content, cancellationToken);
            await EnsureSuccessOrSurfaceAsync(response,
                $"RemoveAppComponents ({actionComponents.Count} non-entity components) from app ({appId})",
                cancellationToken);
        }

        foreach (var entity in entityDetachments)
        {
            // Look up the join row PK for this (app, entity) pair, then DELETE
            // it. Direct DELETE is safe because the join row carries no
            // referential integrity beyond the FK to the app and the metadata
            // id we already know.
            var lookupUrl = $"{ApiBase}appmodulecomponents"
                + $"?$select=appmodulecomponentid"
                + $"&$filter=_appmoduleidunique_value eq {appId} and objectid eq {entity.Id} and componenttype eq 1"
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
                continue;
            }
            var joinId = rows[0].GetProperty("appmodulecomponentid").GetGuid();
            var deleteResponse = await _http.DeleteAsync(
                $"{ApiBase}appmodulecomponents({joinId})", cancellationToken);
            await EnsureSuccessOrSurfaceAsync(deleteResponse,
                $"Delete appmodulecomponent {joinId} (entity {entity.Id} from app {appId})",
                cancellationToken);
        }
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
    /// Lists every component currently attached to an app module, with a
    /// friendly kind, id, and resolved name where possible.
    ///
    /// We previously called the unbound <c>RetrieveAppComponents</c> action,
    /// but a real session 2026-05-06 confirmed that endpoint is broken in some
    /// Dataverse environments — it silently omits entity-component rows even
    /// when the join rows exist (the maker portal shows the entities, our call
    /// returned only the sitemap). Direct query of the <c>appmodulecomponent</c>
    /// table is the reliable read.
    ///
    /// We then resolve each row's <c>objectid</c> to a name by hitting the
    /// appropriate metadata/row endpoint (EntityDefinitions for entities,
    /// systemforms for forms, etc.), in parallel. Duplicates by
    /// (componenttype, objectid) are collapsed — Dataverse permits multiple
    /// join rows targeting the same component (the user can call the maker's
    /// "Add" twice), but the agent reporting them once is more useful.
    /// </summary>
    public async Task<IReadOnlyList<AppModuleComponentRef>> GetAppComponentsAsync(
        Guid appId,
        CancellationToken cancellationToken = default)
    {
        // Step 1: pull every appmodulecomponent join row for this app.
        var url = $"{ApiBase}appmodulecomponents"
            + "?$select=appmodulecomponentid,componenttype,objectid"
            + $"&$filter=_appmoduleidunique_value eq {appId}";

        var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return Array.Empty<AppModuleComponentRef>();
        await EnsureSuccessOrSurfaceAsync(response, $"List appmodulecomponents for app ({appId})", cancellationToken);

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (!root.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<AppModuleComponentRef>();

        // Dedup by (componenttype, objectid) — multiple join rows can point to
        // the same component if the user repeated the attach action.
        var seen = new HashSet<(int, Guid)>();
        var rawRows = new List<(int ComponentType, Guid ObjectId)>();
        foreach (var element in arr.EnumerateArray())
        {
            var componentType = GetIntProp(element, "componenttype");
            var objectId = GetGuidProp(element, "objectid");
            if (objectId == Guid.Empty) continue;
            if (seen.Add((componentType, objectId)))
            {
                rawRows.Add((componentType, objectId));
            }
        }
        if (rawRows.Count == 0) return Array.Empty<AppModuleComponentRef>();

        // Step 2: resolve each row in parallel. Failed lookups degrade to a
        // bare ref (no name / no logical name) rather than dropping the row,
        // so the agent can still surface that something is attached.
        var resolveTasks = rawRows
            .Select(r => ResolveAppComponentAsync(r.ComponentType, r.ObjectId, cancellationToken))
            .ToList();
        var resolved = await Task.WhenAll(resolveTasks);
        return resolved.Where(c => c is not null).Select(c => c!).ToList().AsReadOnly();
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
                1 => await ResolveEntityComponentAsync(objectId, cancellationToken),
                26 => await ResolveSimpleComponentAsync("savedqueries", objectId,
                    "savedqueryid", "name", "returnedtypecode", "savedquery", cancellationToken),
                29 => await ResolveSimpleComponentAsync("workflows", objectId,
                    "workflowid", "name", "primaryentity", "businessprocessflow", cancellationToken),
                59 => await ResolveSimpleComponentAsync("savedqueryvisualizations", objectId,
                    "savedqueryvisualizationid", "name", "primaryentitytypecode", "savedqueryvisualization", cancellationToken),
                60 => await ResolveSimpleComponentAsync("systemforms", objectId,
                    "formid", "name", "objecttypecode", "systemform", cancellationToken),
                62 => await ResolveSimpleComponentAsync("sitemaps", objectId,
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
        var url = $"{ApiBase}EntityDefinitions(MetadataId={metadataId})?$select=LogicalName,DisplayName";
        var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new AppModuleComponentRef("entity", metadataId, null, null);
        await EnsureSuccessOrSurfaceAsync(response, $"Resolve entity metadata ({metadataId})", cancellationToken);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var logicalName = GetStringProp(doc, "LogicalName");
        var displayName = ExtractLocalizedDisplayName(doc);
        return new AppModuleComponentRef("entity", metadataId, displayName, logicalName);
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

    private static string? ExtractLocalizedDisplayName(JsonElement element)
    {
        if (!element.TryGetProperty("DisplayName", out var dn) || dn.ValueKind != JsonValueKind.Object)
            return null;
        if (!dn.TryGetProperty("UserLocalizedLabel", out var lbl) || lbl.ValueKind != JsonValueKind.Object)
            return null;
        return lbl.TryGetProperty("Label", out var l) && l.ValueKind == JsonValueKind.String
            ? l.GetString()
            : null;
    }

    private static Guid GetGuidProp(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var p)) return Guid.Empty;
        if (p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out var g)) return g;
        return Guid.Empty;
    }

    private static string? GetStringProp(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var p)) return null;
        return p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }

    private static int GetIntProp(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var p)) return 0;
        return p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : 0;
    }

    /// <summary>
    /// Maps a Dataverse <c>appmodulecomponent.componenttype</c> integer to a
    /// friendly kind name. NOTE: this is a SCOPED enum — different from the
    /// broader <see cref="SolutionComponentType"/> codes — and only the values
    /// listed below ever appear on appmodulecomponent rows. Rare future types
    /// fall back to "componenttype-N" so the agent can still report them.
    ///
    /// Source: Microsoft Learn — "AppModuleComponent table reference",
    /// componenttype Choices/Options.
    /// </summary>
    private static string MapComponentTypeToKind(int componentType) => componentType switch
    {
        1 => "entity",                  // Entities
        26 => "savedquery",             // Views
        29 => "businessprocessflow",    // Business Process Flows (NOT workflow — appmodulecomponent's enum is scoped)
        48 => "ribboncommand",          // Command (Ribbon) for Forms, Grids, sub grids
        59 => "savedqueryvisualization",// Charts
        60 => "systemform",             // Forms (incl. dashboards — both live in systemform)
        62 => "sitemap",                // Sitemap
        _ => $"componenttype-{componentType}",
    };

    /// <summary>
    /// Last-resort fallback when an unknown component type appears: scan the
    /// element for any property named like an Id and use the first valid GUID
    /// we find. Better than dropping the component silently.
    /// </summary>
    private static Guid GuessIdFromAnyGuidProperty(JsonElement element)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (!prop.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase)) continue;
            if (prop.Value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(prop.Value.GetString(), out var g)) return g;
        }
        return Guid.Empty;
    }

    private static AppModuleInfo MapInfo(AppModuleResponse r) => new(
        r.AppModuleId,
        r.Name ?? string.Empty,
        r.UniqueName ?? string.Empty,
        r.Description ?? string.Empty,
        r.WebResourceId ?? string.Empty,
        r.ClientType ?? 0,
        r.NavigationType ?? 0,
        r.StateCode ?? 0,
        r.StatusCode ?? 0,
        r.IsManaged ?? false,
        r.PublishedOn);

    private static AppModuleDetails MapDetails(AppModuleResponse r) => new(
        r.AppModuleId,
        r.Name ?? string.Empty,
        r.UniqueName ?? string.Empty,
        r.Description ?? string.Empty,
        r.WebResourceId ?? string.Empty,
        r.ClientType ?? 0,
        r.NavigationType ?? 0,
        r.StateCode ?? 0,
        r.StatusCode ?? 0,
        r.IsManaged ?? false,
        r.PublishedOn,
        r.Descriptor,
        r.ConfigXml);

    private static SiteMapInfo MapSiteMap(SiteMapResponse r) => new(
        r.SiteMapId,
        r.SiteMapName ?? string.Empty,
        r.SiteMapNameUnique ?? string.Empty,
        r.SiteMapXml ?? string.Empty,
        r.IsManaged ?? false);

    /// <summary>Wire-format DTO for appmodule rows. Mirrors the $select fields above —
    /// keep both lists in sync when adding new properties.</summary>
    private sealed class AppModuleResponse
    {
        [JsonPropertyName("appmoduleid")]
        public Guid AppModuleId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("uniquename")]
        public string? UniqueName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("_webresourceid_value")]
        public string? WebResourceId { get; set; }

        [JsonPropertyName("clienttype")]
        public int? ClientType { get; set; }

        [JsonPropertyName("navigationtype")]
        public int? NavigationType { get; set; }

        [JsonPropertyName("statecode")]
        public int? StateCode { get; set; }

        [JsonPropertyName("statuscode")]
        public int? StatusCode { get; set; }

        [JsonPropertyName("ismanaged")]
        public bool? IsManaged { get; set; }

        [JsonPropertyName("publishedon")]
        public DateTime? PublishedOn { get; set; }

        [JsonPropertyName("descriptor")]
        public string? Descriptor { get; set; }

        [JsonPropertyName("configxml")]
        public string? ConfigXml { get; set; }
    }

    private sealed class SiteMapResponse
    {
        [JsonPropertyName("sitemapid")]
        public Guid SiteMapId { get; set; }

        [JsonPropertyName("sitemapname")]
        public string? SiteMapName { get; set; }

        [JsonPropertyName("sitemapnameunique")]
        public string? SiteMapNameUnique { get; set; }

        [JsonPropertyName("sitemapxml")]
        public string? SiteMapXml { get; set; }

        [JsonPropertyName("ismanaged")]
        public bool? IsManaged { get; set; }
    }

    private sealed class RoleResponse
    {
        [JsonPropertyName("roleid")]
        public Guid RoleId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("_businessunitid_value")]
        public string? BusinessUnitId { get; set; }

        [JsonPropertyName("ismanaged")]
        public bool? IsManaged { get; set; }
    }
}
