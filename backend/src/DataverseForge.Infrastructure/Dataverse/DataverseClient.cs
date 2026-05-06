using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed partial class DataverseClient : IDataverseClient
{
    // Alias of DataverseConstants.ApiBase brought into scope so partials can use
    // the bare {ApiBase} in interpolated URLs without prefixing the class name.
    private const string ApiBase = DataverseConstants.ApiBase;

    /// <summary>
    /// Builds the absolute API base URL (e.g.
    /// <c>https://org.crm.dynamics.com/api/data/v9.2/</c>) by prepending
    /// the HttpClient's BaseAddress to the relative <see cref="ApiBase"/>
    /// constant. Used for OData <c>@odata.id</c> reference values which
    /// Dataverse rejects in relative form (real session 2026-05-07: app-
    /// role assign returned a generic "payload error" when the @odata.id
    /// pointed at a relative path).
    /// </summary>
    private string BuildAbsoluteApiBase()
    {
        var baseUri = _http.BaseAddress
            ?? throw new InvalidOperationException("HttpClient.BaseAddress not set; can't build absolute @odata.id reference.");
        var trimmed = baseUri.ToString().TrimEnd('/');
        return $"{trimmed}/{ApiBase}";
    }

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<DataverseOptions> _options;
    private readonly ILogger<DataverseClient> _logger;

    public DataverseClient(
        HttpClient http,
        IOptionsMonitor<DataverseOptions> options,
        ILogger<DataverseClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Calling WhoAmI endpoint");
        var whoAmI = await _http.GetFromJsonAsync<WhoAmIResponse>(
            $"{ApiBase}WhoAmI()", cancellationToken)
            ?? throw new InvalidOperationException("WhoAmI returned null");

        _logger.LogDebug("WhoAmI succeeded: OrgId={OrganizationId}", whoAmI.OrganizationId);

        // organizations entity doesn't have a 'version' column — Dataverse exposes the
        // platform version via the unbound function RetrieveVersion() instead.
        // NOTE: `friendlyname` cannot be reliably $select'ed on this entity in some
        // Dataverse environments (returns 400 Bad Request). We pull the friendly name
        // from RetrieveCurrentOrganization() below — same endpoint the health probe uses.
        var orgsResponse = await _http.GetFromJsonAsync<ODataResponse<OrganizationResponse>>(
            $"{ApiBase}organizations?$select=name,languagecode,_basecurrencyid_value,isauditenabled",
            cancellationToken)
            ?? throw new InvalidOperationException("Organizations query returned null");

        var org = orgsResponse.Value.FirstOrDefault()
            ?? throw new InvalidOperationException("No organization found");

        // Best-effort friendly name fetch via RetrieveCurrentOrganization — falls back to
        // org.Name if it fails. `org.Name` is the internal unique name (same as the URL
        // subdomain), so we only use it when nothing better is available.
        string displayName = org.Name;
        try
        {
            var orgRes = await _http.GetFromJsonAsync<RetrieveCurrentOrganizationResponse>(
                $"{ApiBase}RetrieveCurrentOrganization(AccessType=Microsoft.Dynamics.CRM.EndpointAccessType'Default')",
                cancellationToken);
            var friendly = orgRes?.Detail?.FriendlyName;
            if (!string.IsNullOrWhiteSpace(friendly))
                displayName = friendly;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RetrieveCurrentOrganization failed; falling back to org.Name");
        }

        // Best-effort version fetch — non-fatal if it fails
        string version = string.Empty;
        try
        {
            var versionRes = await _http.GetFromJsonAsync<RetrieveVersionResponse>(
                $"{ApiBase}RetrieveVersion()", cancellationToken);
            version = versionRes?.Version ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RetrieveVersion failed; continuing with empty version");
        }

        return new EnvironmentInfo(
            OrganizationName: displayName,
            EnvironmentUrl: _options.CurrentValue.EnvironmentUrl,
            BaseLanguageLcid: org.LanguageCode,
            DataverseVersion: version,
            Region: string.Empty,
            BaseCurrencyCode: org.BaseCurrencyId ?? string.Empty,
            IsAuditEnabled: org.IsAuditEnabled ?? false);
    }

    private sealed class RetrieveVersionResponse
    {
        [JsonPropertyName("Version")]
        public string Version { get; set; } = string.Empty;
    }

    private async Task<int> GetSolutionComponentCountAsync(Guid solutionId, CancellationToken cancellationToken)
    {
        try
        {
            // Dataverse: `/$count` returns Int32 and doesn't accept $filter. The supported
            // filtered-count form is `?$filter=...&$count=true&$top=1` on the collection
            // endpoint — the response includes `@odata.count` with the total.
            var url = $"{ApiBase}solutioncomponents?$filter=_solutionid_value eq {solutionId}&$count=true&$top=1";
            var doc = await _http.GetFromJsonAsync<System.Text.Json.JsonElement>(url, cancellationToken);
            return doc.TryGetProperty("@odata.count", out var c) && c.TryGetInt32(out var n) ? n : 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch component count for solution {SolutionId}", solutionId);
            return 0;
        }
    }

    /// <summary>
    /// Throws <see cref="HttpRequestException"/> if the response is non-success,
    /// embedding the body in the message. Without this, EnsureSuccessStatusCode()
    /// throws "Response status code does not indicate success: 400 (Bad Request)"
    /// with NO body — and Dataverse's actual reason ("invalid attribute X",
    /// "field Y not found on entity Z") is exactly the body. The agent then has
    /// nothing actionable to retry against.
    ///
    /// Best-effort extracts `error.message` from a Dataverse-shaped JSON body;
    /// falls back to the raw body if it's not JSON.
    /// </summary>
    private static async Task EnsureSuccessOrSurfaceAsync(
        HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            body = string.Empty;
        }

        var detail = ExtractDataverseErrorMessage(body) ?? Truncate(body, 800);
        var msg = string.IsNullOrWhiteSpace(detail)
            ? $"{operation} failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
            : $"{operation} failed: HTTP {(int)response.StatusCode} — {detail}";

        throw new HttpRequestException(msg, inner: null, statusCode: response.StatusCode);
    }

    private static string? ExtractDataverseErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            // Dataverse OData v4 error envelope: { "error": { "code": "...", "message": "..." } }
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var message = m.GetString();
                    if (err.TryGetProperty("code", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var code = c.GetString();
                        return string.IsNullOrWhiteSpace(code) ? message : $"{code}: {message}";
                    }
                    return message;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Body wasn't JSON — caller will use the raw body.
        }
        return null;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max] + "…";

    private static Guid ExtractMetadataIdFromHeader(HttpResponseMessage response)
    {
        // Dataverse returns the created entity GUID in the OData-EntityId header
        // Format: https://org.crm.dynamics.com/api/data/v9.2/EntityDefinitions(guid)
        if (response.Headers.TryGetValues("OData-EntityId", out var values))
        {
            var entityIdUrl = values.First();
            var guidStart = entityIdUrl.LastIndexOf('(') + 1;
            var guidEnd = entityIdUrl.LastIndexOf(')');
            if (guidStart > 0 && guidEnd > guidStart)
            {
                return Guid.Parse(entityIdUrl[guidStart..guidEnd]);
            }
        }

        throw new InvalidOperationException("OData-EntityId header not found in response");
    }

    private static string ExtractLabel(LocalizedLabel? label)
    {
        return label?.UserLocalizedLabel?.Label ?? string.Empty;
    }

    #region OData DTOs

    private sealed class TableMetadataResponse
    {
        [JsonPropertyName("MetadataId")]
        public Guid MetadataId { get; set; }

        [JsonPropertyName("LogicalName")]
        public string LogicalName { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")]
        public LocalizedLabel? DisplayName { get; set; }

        [JsonPropertyName("SchemaName")]
        public string SchemaName { get; set; } = string.Empty;

        [JsonPropertyName("OwnershipType")]
        public string? OwnershipType { get; set; }

        [JsonPropertyName("HasNotes")]
        public bool HasNotes { get; set; }

        [JsonPropertyName("HasActivities")]
        public bool HasActivities { get; set; }

        [JsonPropertyName("ChangeTrackingEnabled")]
        public bool ChangeTrackingEnabled { get; set; }

        [JsonPropertyName("IsAuditEnabled")]
        public BooleanManagedProperty? IsAuditEnabled { get; set; }

        [JsonPropertyName("IsActivity")]
        public bool IsActivity { get; set; }

        [JsonPropertyName("IsActivityParty")]
        public bool IsActivityParty { get; set; }

        [JsonPropertyName("IsLogicalEntity")]
        public bool IsLogicalEntity { get; set; }

        [JsonPropertyName("IsRenameable")]
        public BooleanManagedProperty? IsRenameable { get; set; }
    }

    private sealed class FieldMetadataResponse
    {
        [JsonPropertyName("MetadataId")]
        public Guid MetadataId { get; set; }

        [JsonPropertyName("LogicalName")]
        public string LogicalName { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")]
        public LocalizedLabel? DisplayName { get; set; }

        [JsonPropertyName("AttributeTypeName")]
        public AttributeTypeValue? AttributeTypeName { get; set; }

        [JsonPropertyName("RequiredLevel")]
        public RequiredLevelValue? RequiredLevel { get; set; }

        [JsonPropertyName("MaxLength")]
        public int? MaxLength { get; set; }

        [JsonPropertyName("IsSearchable")]
        public bool IsSearchable { get; set; }

        [JsonPropertyName("IsAuditEnabled")]
        public BooleanManagedProperty? IsAuditEnabled { get; set; }

        [JsonPropertyName("IsSecured")]
        public bool IsSecured { get; set; }
    }

    private sealed class RelationshipMetadataResponse
    {
        [JsonPropertyName("SchemaName")]
        public string SchemaName { get; set; } = string.Empty;

        [JsonPropertyName("ReferencedEntity")]
        public string ReferencedEntity { get; set; } = string.Empty;

        [JsonPropertyName("ReferencedAttribute")]
        public string ReferencedAttribute { get; set; } = string.Empty;

        [JsonPropertyName("ReferencingEntity")]
        public string ReferencingEntity { get; set; } = string.Empty;

        [JsonPropertyName("ReferencingAttribute")]
        public string ReferencingAttribute { get; set; } = string.Empty;

        [JsonPropertyName("CascadeConfiguration")]
        public CascadeConfigResponse? CascadeConfiguration { get; set; }
    }

    private sealed class CascadeConfigResponse
    {
        [JsonPropertyName("Assign")]
        public string Assign { get; set; } = string.Empty;

        [JsonPropertyName("Delete")]
        public string Delete { get; set; } = string.Empty;

        [JsonPropertyName("Reparent")]
        public string Reparent { get; set; } = string.Empty;

        [JsonPropertyName("Merge")]
        public string Merge { get; set; } = string.Empty;

        [JsonPropertyName("Share")]
        public string Share { get; set; } = string.Empty;

        [JsonPropertyName("Unshare")]
        public string Unshare { get; set; } = string.Empty;
    }

    private sealed class AddSolutionComponentRequest
    {
        [JsonPropertyName("ComponentId")]
        public Guid ComponentId { get; set; }

        [JsonPropertyName("ComponentType")]
        public int ComponentType { get; set; }

        [JsonPropertyName("SolutionUniqueName")]
        public string SolutionUniqueName { get; set; } = string.Empty;

        [JsonPropertyName("AddRequiredComponents")]
        public bool AddRequiredComponents { get; set; }

        // When true, Dataverse adds ONLY the root component (e.g. just the table)
        // without sweeping in every field, form, view, relationship attached to it.
        // Critical for "add this existing table to my solution" — users expect the
        // table only, plus a separate confirmation before pulling in subcomponents.
        [JsonPropertyName("DoNotIncludeSubcomponents")]
        public bool DoNotIncludeSubcomponents { get; set; }

        [JsonPropertyName("IncludedComponentSettingsValues")]
        public string[]? IncludedComponentSettingsValues { get; set; }
    }

    private sealed class WhoAmIResponse
    {
        [JsonPropertyName("OrganizationId")]
        public Guid OrganizationId { get; set; }

        [JsonPropertyName("BusinessUnitId")]
        public Guid BusinessUnitId { get; set; }

        [JsonPropertyName("UserId")]
        public Guid UserId { get; set; }
    }

    private sealed class ODataResponse<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = [];
    }

    private sealed class EntityKeyResponse
    {
        [JsonPropertyName("MetadataId")]
        public Guid MetadataId { get; set; }

        [JsonPropertyName("LogicalName")]
        public string? LogicalName { get; set; }

        [JsonPropertyName("DisplayName")]
        public LocalizedLabel? DisplayName { get; set; }

        [JsonPropertyName("KeyAttributes")]
        public string[]? KeyAttributes { get; set; }
    }

    private sealed class OrganizationResponse
    {
        // `name` is the internal unique name (e.g. "org2736fd3b") — same as URL subdomain.
        // For the user-visible friendly name we call RetrieveCurrentOrganization separately.
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("languagecode")]
        public int LanguageCode { get; set; }

        [JsonPropertyName("_basecurrencyid_value")]
        public string? BaseCurrencyId { get; set; }

        [JsonPropertyName("isauditenabled")]
        public bool? IsAuditEnabled { get; set; }
    }

    private sealed class RetrieveCurrentOrganizationResponse
    {
        [JsonPropertyName("Detail")]
        public OrganizationDetail? Detail { get; set; }
    }

    private sealed class OrganizationDetail
    {
        [JsonPropertyName("FriendlyName")]
        public string? FriendlyName { get; set; }
    }

    private sealed class BooleanManagedProperty
    {
        [JsonPropertyName("Value")]
        public bool Value { get; set; }

        [JsonPropertyName("CanBeChanged")]
        public bool CanBeChanged { get; set; }
    }

    private sealed class SolutionResponse
    {
        [JsonPropertyName("solutionid")]
        public Guid SolutionId { get; set; }

        [JsonPropertyName("uniquename")]
        public string UniqueName { get; set; } = string.Empty;

        [JsonPropertyName("friendlyname")]
        public string FriendlyName { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("ismanaged")]
        public bool IsManaged { get; set; }

        [JsonPropertyName("modifiedon")]
        public DateTime ModifiedOn { get; set; }

        [JsonPropertyName("publisherid")]
        public PublisherRef? PublisherId { get; set; }
    }

    private sealed class PublisherRef
    {
        [JsonPropertyName("publisherid")]
        public Guid PublisherId { get; set; }

        [JsonPropertyName("friendlyname")]
        public string FriendlyName { get; set; } = string.Empty;

        [JsonPropertyName("uniquename")]
        public string UniqueName { get; set; } = string.Empty;

        [JsonPropertyName("customizationprefix")]
        public string CustomizationPrefix { get; set; } = string.Empty;
    }

    private sealed class PublisherResponse
    {
        [JsonPropertyName("publisherid")]
        public Guid PublisherId { get; set; }

        [JsonPropertyName("friendlyname")]
        public string FriendlyName { get; set; } = string.Empty;

        [JsonPropertyName("uniquename")]
        public string UniqueName { get; set; } = string.Empty;

        [JsonPropertyName("customizationprefix")]
        public string CustomizationPrefix { get; set; } = string.Empty;

        [JsonPropertyName("customizationoptionvalueprefix")]
        public int CustomizationOptionValuePrefix { get; set; }
    }

    private sealed class EntityDefinitionResponse
    {
        [JsonPropertyName("LogicalName")]
        public string LogicalName { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")]
        public LocalizedLabel? DisplayName { get; set; }

        [JsonPropertyName("SchemaName")]
        public string SchemaName { get; set; } = string.Empty;

        [JsonPropertyName("IsCustomEntity")]
        public bool IsCustomEntity { get; set; }

        [JsonPropertyName("IsManaged")]
        public bool IsManaged { get; set; }

        [JsonPropertyName("OwnershipType")]
        public string? OwnershipType { get; set; }

        [JsonPropertyName("PrimaryIdAttribute")]
        public string? PrimaryIdAttribute { get; set; }

        [JsonPropertyName("PrimaryNameAttribute")]
        public string? PrimaryNameAttribute { get; set; }

        [JsonPropertyName("HasNotes")]
        public bool HasNotes { get; set; }

        [JsonPropertyName("HasActivities")]
        public bool HasActivities { get; set; }

        [JsonPropertyName("ChangeTrackingEnabled")]
        public bool ChangeTrackingEnabled { get; set; }
    }

    private sealed class EntityDetailResponse
    {
        [JsonPropertyName("LogicalName")]
        public string LogicalName { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")]
        public LocalizedLabel? DisplayName { get; set; }

        [JsonPropertyName("SchemaName")]
        public string SchemaName { get; set; } = string.Empty;

        [JsonPropertyName("IsCustomEntity")]
        public bool IsCustomEntity { get; set; }

        [JsonPropertyName("IsManaged")]
        public bool IsManaged { get; set; }

        [JsonPropertyName("OwnershipType")]
        public string? OwnershipType { get; set; }

        [JsonPropertyName("PrimaryIdAttribute")]
        public string? PrimaryIdAttribute { get; set; }

        [JsonPropertyName("PrimaryNameAttribute")]
        public string? PrimaryNameAttribute { get; set; }

        [JsonPropertyName("Attributes")]
        public List<AttributeResponse>? Attributes { get; set; }

        [JsonPropertyName("Keys")]
        public List<AlternateKeyResponse>? Keys { get; set; }

        [JsonPropertyName("OneToManyRelationships")]
        public List<RelationshipODataResponse>? OneToManyRelationships { get; set; }

        [JsonPropertyName("ManyToOneRelationships")]
        public List<RelationshipODataResponse>? ManyToOneRelationships { get; set; }
    }

    private sealed class AttributeResponse
    {
        [JsonPropertyName("LogicalName")]
        public string LogicalName { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")]
        public LocalizedLabel? DisplayName { get; set; }

        [JsonPropertyName("AttributeTypeName")]
        public AttributeTypeValue? AttributeTypeName { get; set; }

        [JsonPropertyName("IsCustomAttribute")]
        public bool IsCustomAttribute { get; set; }

        [JsonPropertyName("RequiredLevel")]
        public RequiredLevelValue? RequiredLevel { get; set; }

        [JsonPropertyName("MaxLength")]
        public int? MaxLength { get; set; }

        [JsonPropertyName("IsSearchable")]
        public bool IsSearchable { get; set; }

        [JsonPropertyName("IsSecured")]
        public bool IsSecured { get; set; }
    }

    private sealed class AttributeTypeValue
    {
        [JsonPropertyName("Value")]
        public string Value { get; set; } = string.Empty;
    }

    private sealed class RequiredLevelValue
    {
        [JsonPropertyName("Value")]
        public string Value { get; set; } = "None";
    }

    private sealed class AlternateKeyResponse
    {
        [JsonPropertyName("LogicalName")]
        public string LogicalName { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")]
        public LocalizedLabel? DisplayName { get; set; }

        [JsonPropertyName("KeyAttributes")]
        public List<string>? KeyAttributes { get; set; }
    }

    private sealed class RelationshipODataResponse
    {
        [JsonPropertyName("SchemaName")]
        public string SchemaName { get; set; } = string.Empty;

        [JsonPropertyName("ReferencedEntity")]
        public string ReferencedEntity { get; set; } = string.Empty;

        [JsonPropertyName("ReferencedAttribute")]
        public string ReferencedAttribute { get; set; } = string.Empty;

        [JsonPropertyName("ReferencingEntity")]
        public string ReferencingEntity { get; set; } = string.Empty;

        [JsonPropertyName("ReferencingAttribute")]
        public string ReferencingAttribute { get; set; } = string.Empty;

        // The OData navigation property name on the referencing entity. This is what
        // lookup writes use with @odata.bind — it is NOT the same as the lookup column's
        // logical name. Required to translate `eppc_contactid: <guid>` → `eppc_ContactId@odata.bind`.
        [JsonPropertyName("ReferencingEntityNavigationPropertyName")]
        public string? ReferencingEntityNavigationPropertyName { get; set; }
    }

    private sealed class LocalizedLabel
    {
        [JsonPropertyName("UserLocalizedLabel")]
        public LocalizedLabelItem? UserLocalizedLabel { get; set; }
    }

    private sealed class LocalizedLabelItem
    {
        [JsonPropertyName("Label")]
        public string Label { get; set; } = string.Empty;
    }

    private sealed class SystemFormResponse
    {
        [JsonPropertyName("formid")]
        public Guid FormId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("isdefault")]
        public bool IsDefault { get; set; }

        [JsonPropertyName("formxml")]
        public string? FormXml { get; set; }
    }

    private sealed class SavedQueryResponse
    {
        [JsonPropertyName("savedqueryid")]
        public Guid SavedQueryId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("fetchxml")]
        public string? FetchXml { get; set; }

        [JsonPropertyName("layoutxml")]
        public string? LayoutXml { get; set; }
    }

    private sealed class WorkflowResponse
    {
        [JsonPropertyName("workflowid")]
        public Guid WorkflowId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("primaryentity")]
        public string PrimaryEntity { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public int Category { get; set; }

        [JsonPropertyName("statecode")]
        public int StateCode { get; set; }

        [JsonPropertyName("statuscode")]
        public int StatusCode { get; set; }
    }

    #endregion
}
