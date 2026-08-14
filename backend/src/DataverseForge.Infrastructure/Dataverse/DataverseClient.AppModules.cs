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

    /// <summary>
    /// Named constants for the <c>appmodulecomponent.componenttype</c>
    /// picklist (NOT the broader <c>SolutionComponentType</c> enum — they
    /// share some numeric values but the scopes are different). Source:
    /// Microsoft Learn — "AppModuleComponent table reference",
    /// componenttype Choices/Options.
    /// </summary>
    private static class AppModuleComponentType
    {
        public const int Entity = 1;
        public const int SavedQuery = 26;
        public const int BusinessProcessFlow = 29;
        public const int RibbonCommand = 48;
        public const int SavedQueryVisualization = 59;
        public const int SystemForm = 60;
        public const int SiteMap = 62;
    }

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

        // Sequence the app read first because GetAppComponentsAsync needs the
        // descriptor JSON it returns (avoids a duplicate appmodule fetch).
        // The remaining 3 reads run in parallel — they don't depend on the
        // descriptor and a partial failure on any one degrades to its
        // fallback rather than killing the whole composite read.
        var app = await GetAppAsync(appId, cancellationToken);
        if (app is null) return null;

        var componentsTask = SafeReadAsync(
            () => GetAppComponentsAsync(appId, app.Descriptor, cancellationToken),
            (IReadOnlyList<AppModuleComponentRef>)Array.Empty<AppModuleComponentRef>(),
            $"GetAppComponents AppId={appId}");
        var sitemapTask = SafeReadAsync<SiteMapInfo?>(
            () => GetSiteMapForAppAsync(appId, cancellationToken),
            null,
            $"GetSiteMapForApp AppId={appId}");
        var rolesTask = SafeReadAsync(
            () => ListAppRolesAsync(appId, cancellationToken),
            (IReadOnlyList<AppModuleRoleInfo>)Array.Empty<AppModuleRoleInfo>(),
            $"ListAppRoles AppId={appId}");

        // Await each individually after WhenAll completes — keeps the
        // observable behavior (all three run concurrently, all three errors
        // are caught by SafeReadAsync) while removing the `.Result` calls
        // that would deadlock if a future maintainer dropped the WhenAll.
        await Task.WhenAll(componentsTask, sitemapTask, rolesTask);
        var components = await componentsTask;
        var sitemap = await sitemapTask;
        var roles = await rolesTask;

        return new AppModuleFullDetails(app, components, sitemap, roles);
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

        // If-Match: * forces Dataverse to apply only if the row exists.
        // Without it, PATCH on a missing id silently upserts — which is the
        // opposite of what an "update" tool should do.
        await PatchVoidAsync($"{ApiBase}appmodules({appId})", webApiPayloadJson, $"Update app ({appId})", cancellationToken, ifMatch: true);
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

        // Some environments / versions don't expose the ValidateApp Web API action
        // at all — the POST 404s ("No HTTP resource was found ... /ValidateApp").
        // That is NOT an app failure: the app row was created and is verified
        // separately. Returning a hard error here made the Tester FAIL a perfectly
        // good app on a missing platform endpoint. Treat 404 as "deep validation
        // unavailable" and report a non-blocking Info issue instead.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "ValidateApp action is not available in this environment (HTTP 404) for app {AppId}; skipping deep validation",
                appId);
            return new AppValidationResult(appId, IsValid: true, Issues: new[]
            {
                new AppValidationIssue(
                    "Info",
                    "Deep ValidateApp validation is not available in this environment (HTTP 404); app existence is verified separately.",
                    null, null, null)
            });
        }

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
        await PostVoidAsync($"{ApiBase}PublishXml", json, $"PublishApp ({appId})", cancellationToken);
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

        await PatchVoidAsync($"{ApiBase}sitemaps({siteMapId})", webApiPayloadJson, $"Update sitemap ({siteMapId})", cancellationToken, ifMatch: true);
    }

    public async Task DeleteSiteMapAsync(Guid siteMapId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting sitemap {SiteMapId}", siteMapId);

        // The appmodule's cascade clears `appmodule_sitemap` but leaves the
        // sitemap row, so a scaffold-created sitemap survives DeleteApp and
        // blocks re-scaffold (duplicate sitemapnameunique). Direct DELETE on
        // the sitemap row is required after the app is gone.
        var response = await _http.DeleteAsync($"{ApiBase}sitemaps({siteMapId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Delete sitemap ({siteMapId})", cancellationToken);
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
        AppModuleComponentType.Entity => "entity",
        AppModuleComponentType.SavedQuery => "savedquery",
        AppModuleComponentType.BusinessProcessFlow => "businessprocessflow",
        AppModuleComponentType.RibbonCommand => "ribboncommand",
        AppModuleComponentType.SavedQueryVisualization => "savedqueryvisualization",
        AppModuleComponentType.SystemForm => "systemform",
        AppModuleComponentType.SiteMap => "sitemap",
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
