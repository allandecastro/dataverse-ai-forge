using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed class DataverseClient : IDataverseClient
{
    private readonly HttpClient _http;
    private readonly IOptions<DataverseOptions> _options;
    private readonly ILogger<DataverseClient> _logger;

    public DataverseClient(
        HttpClient http,
        IOptions<DataverseOptions> options,
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
            "api/data/v9.2/WhoAmI", cancellationToken)
            ?? throw new InvalidOperationException("WhoAmI returned null");

        _logger.LogDebug("WhoAmI succeeded: OrgId={OrganizationId}", whoAmI.OrganizationId);

        var orgsResponse = await _http.GetFromJsonAsync<ODataResponse<OrganizationResponse>>(
            "api/data/v9.2/organizations?$select=name,languagecode,version,_basecurrencyid_value,isauditenabled",
            cancellationToken)
            ?? throw new InvalidOperationException("Organizations query returned null");

        var org = orgsResponse.Value.FirstOrDefault()
            ?? throw new InvalidOperationException("No organization found");

        return new EnvironmentInfo(
            OrganizationName: org.Name,
            EnvironmentUrl: _options.Value.EnvironmentUrl,
            BaseLanguageLcid: org.LanguageCode,
            DataverseVersion: org.Version,
            Region: string.Empty,
            BaseCurrencyCode: org.BaseCurrencyId ?? string.Empty,
            IsAuditEnabled: org.IsAuditEnabled?.Value ?? false);
    }

    public async Task<IReadOnlyList<SolutionInfo>> GetSolutionsListAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying unmanaged solutions");

        var response = await _http.GetFromJsonAsync<ODataResponse<SolutionResponse>>(
            "api/data/v9.2/solutions?$filter=ismanaged eq false&$select=solutionid,uniquename,friendlyname,version,ismanaged,modifiedon&$expand=publisherid($select=friendlyname)&$orderby=modifiedon desc",
            cancellationToken)
            ?? throw new InvalidOperationException("Solutions query returned null");

        _logger.LogDebug("Found {Count} unmanaged solutions", response.Value.Count);

        return response.Value
            .Select(s => new SolutionInfo(
                SolutionId: s.SolutionId,
                UniqueName: s.UniqueName,
                DisplayName: s.FriendlyName,
                Version: s.Version,
                PublisherName: s.PublisherId?.FriendlyName ?? string.Empty,
                IsManaged: s.IsManaged,
                ComponentCount: 0,
                ModifiedOn: s.ModifiedOn))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<PublisherInfo>> GetPublishersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying non-readonly publishers");

        var response = await _http.GetFromJsonAsync<ODataResponse<PublisherResponse>>(
            "api/data/v9.2/publishers?$filter=isreadonly eq false&$select=publisherid,friendlyname,uniquename,customizationprefix,customizationoptionvalueprefix",
            cancellationToken)
            ?? throw new InvalidOperationException("Publishers query returned null");

        _logger.LogDebug("Found {Count} publishers", response.Value.Count);

        return response.Value
            .Select(p => new PublisherInfo(
                PublisherId: p.PublisherId,
                DisplayName: p.FriendlyName,
                UniqueName: p.UniqueName,
                Prefix: p.CustomizationPrefix,
                OptionValuePrefix: p.CustomizationOptionValuePrefix))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesListAsync(
        string filter = "all",
        string? solutionName = null,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying entity definitions with filter={Filter}, solutionName={SolutionName}, searchTerm={SearchTerm}",
            filter, solutionName, searchTerm);

        var baseUrl = "api/data/v9.2/EntityDefinitions?$select=LogicalName,DisplayName,SchemaName,IsCustomEntity,IsManaged,OwnershipType,PrimaryIdAttribute,PrimaryNameAttribute,HasNotes,HasActivities,ChangeTrackingEnabled";

        var filters = new List<string>();

        switch (filter.ToLowerInvariant())
        {
            case "custom":
                filters.Add("IsCustomEntity eq true");
                break;
            case "oob":
                filters.Add("IsCustomEntity eq false");
                break;
            case "solution":
                if (string.IsNullOrWhiteSpace(solutionName))
                    throw new ArgumentException("solutionName is required when filter is 'solution'", nameof(solutionName));
                filters.Add($"Microsoft.Dynamics.CRM.SolutionComponentDefinition/SolutionUniqueName eq '{solutionName}'");
                break;
            case "all":
                break;
            default:
                throw new ArgumentException($"Invalid filter value: {filter}. Expected: all, custom, oob, solution", nameof(filter));
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var escapedTerm = searchTerm.Replace("'", "''");
            filters.Add($"(contains(LogicalName,'{escapedTerm}') or contains(DisplayName/UserLocalizedLabel/Label,'{escapedTerm}'))");
        }

        if (filters.Count > 0)
        {
            baseUrl += "&$filter=" + string.Join(" and ", filters);
        }

        var response = await _http.GetFromJsonAsync<ODataResponse<EntityDefinitionResponse>>(
            baseUrl, cancellationToken)
            ?? throw new InvalidOperationException("EntityDefinitions query returned null");

        _logger.LogDebug("Found {Count} entities", response.Value.Count);

        return response.Value
            .Select(e => new TableInfo(
                LogicalName: e.LogicalName,
                DisplayName: ExtractLabel(e.DisplayName),
                SchemaName: e.SchemaName,
                IsCustom: e.IsCustomEntity,
                IsManaged: e.IsManaged,
                OwnershipType: e.OwnershipType ?? string.Empty,
                PrimaryIdAttribute: e.PrimaryIdAttribute ?? string.Empty,
                PrimaryNameAttribute: e.PrimaryNameAttribute ?? string.Empty,
                HasNotes: e.HasNotes,
                HasActivities: e.HasActivities,
                ChangeTrackingEnabled: e.ChangeTrackingEnabled))
            .ToList()
            .AsReadOnly();
    }

    public async Task<TableDetails> GetTableDetailsAsync(string tableLogicalName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying entity details for {TableLogicalName}", tableLogicalName);

        var url = $"api/data/v9.2/EntityDefinitions(LogicalName='{tableLogicalName}')"
            + "?$expand="
            + "Attributes($select=LogicalName,DisplayName,AttributeTypeName,IsCustomAttribute,RequiredLevel,MaxLength,IsSearchable),"
            + "Keys($select=LogicalName,DisplayName,KeyAttributes),"
            + "OneToManyRelationships($select=SchemaName,ReferencedEntity,ReferencedAttribute,ReferencingEntity,ReferencingAttribute),"
            + "ManyToOneRelationships($select=SchemaName,ReferencedEntity,ReferencedAttribute,ReferencingEntity,ReferencingAttribute)";

        var entity = await _http.GetFromJsonAsync<EntityDetailResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException($"EntityDefinition for '{tableLogicalName}' returned null");

        var columns = (entity.Attributes ?? [])
            .Select(a => new ColumnInfo(
                LogicalName: a.LogicalName,
                DisplayName: ExtractLabel(a.DisplayName),
                AttributeType: a.AttributeTypeName?.Value ?? string.Empty,
                IsCustom: a.IsCustomAttribute,
                RequiredLevel: a.RequiredLevel?.Value ?? "None",
                MaxLength: a.MaxLength,
                IsSearchable: a.IsSearchable))
            .ToList()
            .AsReadOnly();

        var relationships = new List<RelationshipInfo>();

        foreach (var r in entity.OneToManyRelationships ?? [])
        {
            relationships.Add(new RelationshipInfo(
                SchemaName: r.SchemaName,
                RelationshipType: "OneToMany",
                ReferencedEntity: r.ReferencedEntity,
                ReferencedAttribute: r.ReferencedAttribute,
                ReferencingEntity: r.ReferencingEntity,
                ReferencingAttribute: r.ReferencingAttribute));
        }

        foreach (var r in entity.ManyToOneRelationships ?? [])
        {
            relationships.Add(new RelationshipInfo(
                SchemaName: r.SchemaName,
                RelationshipType: "ManyToOne",
                ReferencedEntity: r.ReferencedEntity,
                ReferencedAttribute: r.ReferencedAttribute,
                ReferencingEntity: r.ReferencingEntity,
                ReferencingAttribute: r.ReferencingAttribute));
        }

        var alternateKeys = (entity.Keys ?? [])
            .Select(k => new AlternateKeyInfo(
                LogicalName: k.LogicalName,
                DisplayName: ExtractLabel(k.DisplayName),
                KeyAttributes: (k.KeyAttributes ?? []).AsReadOnly()))
            .ToList()
            .AsReadOnly();

        return new TableDetails(
            LogicalName: entity.LogicalName,
            DisplayName: ExtractLabel(entity.DisplayName),
            SchemaName: entity.SchemaName,
            IsCustom: entity.IsCustomEntity,
            IsManaged: entity.IsManaged,
            OwnershipType: entity.OwnershipType ?? string.Empty,
            Columns: columns,
            Relationships: relationships.AsReadOnly(),
            AlternateKeys: alternateKeys);
    }

    public async Task<Guid> CreateTableAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating table via EntityDefinitions");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/EntityDefinitions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task AddSolutionComponentAsync(Guid componentId, int componentType, string solutionUniqueName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Adding component {ComponentId} (type {ComponentType}) to solution {Solution}",
            componentId, componentType, solutionUniqueName);

        var request = new AddSolutionComponentRequest
        {
            ComponentId = componentId,
            ComponentType = componentType,
            SolutionUniqueName = solutionUniqueName,
            AddRequiredComponents = false,
            IncludedComponentSettingsValues = null
        };

        var response = await _http.PostAsJsonAsync("api/data/v9.2/AddSolutionComponent", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Guid> CreateFieldAsync(Guid tableMetadataId, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating field on table {TableMetadataId}", tableMetadataId);

        var url = $"api/data/v9.2/EntityDefinitions({tableMetadataId})/Attributes";
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<Guid> CreateOneToManyRelationshipAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating 1:N relationship via RelationshipDefinitions");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/RelationshipDefinitions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<Guid> CreateManyToManyRelationshipAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating N:N relationship via RelationshipDefinitions");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/RelationshipDefinitions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<Guid> CreateGlobalOptionSetAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating global option set via GlobalOptionSetDefinitions");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/GlobalOptionSetDefinitions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task InsertOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Inserting option value via InsertOptionValue");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/InsertOptionValue", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Guid> CreateAlternateKeyAsync(Guid tableMetadataId, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating alternate key on table {TableMetadataId}", tableMetadataId);

        var url = $"api/data/v9.2/EntityDefinitions({tableMetadataId})/Keys";
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task ModifyFieldAsync(Guid tableMetadataId, Guid fieldMetadataId, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Modifying field {FieldMetadataId} on table {TableMetadataId}", fieldMetadataId, tableMetadataId);

        var url = $"api/data/v9.2/EntityDefinitions({tableMetadataId})/Attributes({fieldMetadataId})";
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PutAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TableMetadata> GetTableMetadataAsync(string tableLogicalName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying table metadata for {TableLogicalName}", tableLogicalName);

        var url = $"api/data/v9.2/EntityDefinitions(LogicalName='{tableLogicalName}')"
            + "?$select=LogicalName,DisplayName,SchemaName,OwnershipType,HasNotes,HasActivities,ChangeTrackingEnabled,IsAuditEnabled";

        var entity = await _http.GetFromJsonAsync<TableMetadataResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException($"EntityDefinition for '{tableLogicalName}' returned null");

        return new TableMetadata(
            LogicalName: entity.LogicalName,
            DisplayName: ExtractLabel(entity.DisplayName),
            SchemaName: entity.SchemaName,
            OwnershipType: entity.OwnershipType ?? string.Empty,
            HasNotes: entity.HasNotes,
            HasActivities: entity.HasActivities,
            ChangeTrackingEnabled: entity.ChangeTrackingEnabled,
            IsAuditEnabled: entity.IsAuditEnabled?.Value ?? false);
    }

    public async Task<FieldMetadata> GetFieldMetadataAsync(string tableLogicalName, string fieldLogicalName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying field metadata for {FieldLogicalName} on {TableLogicalName}", fieldLogicalName, tableLogicalName);

        var url = $"api/data/v9.2/EntityDefinitions(LogicalName='{tableLogicalName}')"
            + $"/Attributes(LogicalName='{fieldLogicalName}')"
            + "?$select=LogicalName,DisplayName,AttributeTypeName,RequiredLevel,MaxLength,IsSearchable,IsAuditEnabled";

        var attr = await _http.GetFromJsonAsync<FieldMetadataResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException($"Attribute '{fieldLogicalName}' on '{tableLogicalName}' returned null");

        return new FieldMetadata(
            LogicalName: attr.LogicalName,
            DisplayName: ExtractLabel(attr.DisplayName),
            AttributeType: attr.AttributeTypeName?.Value ?? string.Empty,
            RequiredLevel: attr.RequiredLevel?.Value ?? "None",
            MaxLength: attr.MaxLength,
            IsSearchable: attr.IsSearchable,
            IsAuditEnabled: attr.IsAuditEnabled?.Value ?? false);
    }

    public async Task<RelationshipMetadata> GetRelationshipMetadataAsync(string relationshipSchemaName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying relationship metadata for {RelationshipSchemaName}", relationshipSchemaName);

        var url = $"api/data/v9.2/RelationshipDefinitions(SchemaName='{relationshipSchemaName}')"
            + "?$select=SchemaName,ReferencedEntity,ReferencedAttribute,ReferencingEntity,ReferencingAttribute,"
            + "CascadeConfiguration";

        var rel = await _http.GetFromJsonAsync<RelationshipMetadataResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException($"Relationship '{relationshipSchemaName}' returned null");

        var cascade = rel.CascadeConfiguration;
        return new RelationshipMetadata(
            SchemaName: rel.SchemaName,
            ReferencedEntity: rel.ReferencedEntity,
            ReferencedAttribute: rel.ReferencedAttribute,
            ReferencingEntity: rel.ReferencingEntity,
            ReferencingAttribute: rel.ReferencingAttribute,
            CascadeAssign: cascade?.Assign ?? string.Empty,
            CascadeDelete: cascade?.Delete ?? string.Empty,
            CascadeReparent: cascade?.Reparent ?? string.Empty,
            CascadeMerge: cascade?.Merge ?? string.Empty,
            CascadeShare: cascade?.Share ?? string.Empty,
            CascadeUnshare: cascade?.Unshare ?? string.Empty);
    }

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
    }

    private sealed class FieldMetadataResponse
    {
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

    private sealed class OrganizationResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("languagecode")]
        public int LanguageCode { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("_basecurrencyid_value")]
        public string? BaseCurrencyId { get; set; }

        [JsonPropertyName("isauditenabled")]
        public BooleanManagedProperty? IsAuditEnabled { get; set; }
    }

    private sealed class BooleanManagedProperty
    {
        [JsonPropertyName("Value")]
        public bool Value { get; set; }
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
        [JsonPropertyName("friendlyname")]
        public string FriendlyName { get; set; } = string.Empty;
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

    #endregion
}
