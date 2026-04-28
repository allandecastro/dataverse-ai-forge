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
            EnvironmentUrl: _options.CurrentValue.EnvironmentUrl,
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

    public async Task<Guid> CreateSolutionAsync(
        string uniqueName,
        string friendlyName,
        string description,
        string version,
        Guid publisherId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating unmanaged solution '{UniqueName}' with publisher {PublisherId}", uniqueName, publisherId);

        var payload = new Dictionary<string, object>
        {
            ["uniquename"] = uniqueName,
            ["friendlyname"] = friendlyName,
            ["description"] = description,
            ["version"] = string.IsNullOrWhiteSpace(version) ? "1.0.0.0" : version,
            ["[email protected]"] = $"/publishers({publisherId})"
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/solutions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractMetadataIdFromHeader(response);
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

    public async Task<IReadOnlyList<FormInfo>> GetFormsListAsync(string tableLogicalName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying forms for {TableLogicalName}", tableLogicalName);

        var url = $"api/data/v9.2/systemforms?$filter=objecttypecode eq '{tableLogicalName}'&$select=formid,name,type,description,isdefault";
        var response = await _http.GetFromJsonAsync<ODataResponse<SystemFormResponse>>(url, cancellationToken)
            ?? throw new InvalidOperationException($"Forms query for '{tableLogicalName}' returned null");

        return response.Value
            .Select(f => new FormInfo(f.FormId, f.Name, MapFormType(f.Type), f.Description ?? string.Empty, f.IsDefault))
            .ToList()
            .AsReadOnly();
    }

    public async Task<FormDefinition> GetFormDefinitionAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying form definition for {FormId}", formId);

        var url = $"api/data/v9.2/systemforms({formId})?$select=formid,name,type,formxml";
        var form = await _http.GetFromJsonAsync<SystemFormResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException($"Form '{formId}' returned null");

        return new FormDefinition(form.FormId, form.Name, MapFormType(form.Type), form.FormXml ?? string.Empty);
    }

    public async Task<Guid> CreateFormAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating form via systemforms");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/systemforms", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<IReadOnlyList<ViewInfo>> GetViewsListAsync(string tableLogicalName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying views for {TableLogicalName}", tableLogicalName);

        var url = $"api/data/v9.2/savedqueries?$filter=returnedtypecode eq '{tableLogicalName}'&$select=savedqueryid,name,description,fetchxml,layoutxml";
        var response = await _http.GetFromJsonAsync<ODataResponse<SavedQueryResponse>>(url, cancellationToken)
            ?? throw new InvalidOperationException($"Views query for '{tableLogicalName}' returned null");

        return response.Value
            .Select(v => new ViewInfo(v.SavedQueryId, v.Name, v.Description ?? string.Empty, v.FetchXml ?? string.Empty, v.LayoutXml ?? string.Empty))
            .ToList()
            .AsReadOnly();
    }

    public async Task<Guid> CreateViewAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating view via savedqueries");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/savedqueries", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<Guid> CreateDashboardAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating dashboard via systemforms");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/systemforms", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<Guid> CreateModelDrivenAppAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating model-driven app via appmodules");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/appmodules", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractMetadataIdFromHeader(response);
    }

    private static string MapFormType(int type) => type switch
    {
        2 => "main",
        5 => "quickCreate",
        6 => "quickView",
        0 => "dashboard",
        _ => $"type_{type}"
    };

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

    // ─── BPF / BUSINESS RULE / CUSTOM API ───

    public async Task<Guid> CreateBpfAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating BPF via workflows endpoint");
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/workflows", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Headers.TryGetValues("OData-EntityId", out var values))
        {
            var entityIdUrl = values.First();
            var guidString = entityIdUrl[(entityIdUrl.LastIndexOf('(') + 1)..entityIdUrl.LastIndexOf(')')];
            return Guid.Parse(guidString);
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return result.GetProperty("workflowid").GetGuid();
    }

    public async Task<Guid> CreateBusinessRuleAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating Business Rule via workflows endpoint");
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/workflows", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Headers.TryGetValues("OData-EntityId", out var values))
        {
            var entityIdUrl = values.First();
            var guidString = entityIdUrl[(entityIdUrl.LastIndexOf('(') + 1)..entityIdUrl.LastIndexOf(')')];
            return Guid.Parse(guidString);
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return result.GetProperty("workflowid").GetGuid();
    }

    public async Task<Guid> CreateCustomApiAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating Custom API");
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/customapis", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Headers.TryGetValues("OData-EntityId", out var values))
        {
            var entityIdUrl = values.First();
            var guidString = entityIdUrl[(entityIdUrl.LastIndexOf('(') + 1)..entityIdUrl.LastIndexOf(')')];
            return Guid.Parse(guidString);
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return result.GetProperty("customapiid").GetGuid();
    }

    public async Task<BpfMetadata> GetBpfMetadataAsync(string bpfName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting BPF metadata for {BpfName}", bpfName);
        var url = $"api/data/v9.2/workflows?$filter=name eq '{bpfName}' and category eq 4&$select=workflowid,name,primaryentity,category,statecode,statuscode";
        var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var items = json.GetProperty("value");

        if (items.GetArrayLength() == 0)
            throw new InvalidOperationException($"BPF '{bpfName}' not found.");

        var bpf = items[0];
        return new BpfMetadata(
            bpf.GetProperty("workflowid").GetGuid(),
            bpf.GetProperty("name").GetString() ?? string.Empty,
            bpf.GetProperty("primaryentity").GetString() ?? string.Empty,
            "BPF",
            bpf.GetProperty("statecode").GetInt32(),
            bpf.GetProperty("statuscode").GetInt32());
    }

    public async Task<IReadOnlyList<SecurityRoleInfo>> GetSecurityRolesAsync(string filter = "all", CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting security roles with filter {Filter}", filter);
        var url = "api/data/v9.2/roles?$select=roleid,name,ismanaged,iscustomizable,_businessunitid_value&$orderby=name";

        if (filter == "custom")
            url += "&$filter=ismanaged eq false";
        else if (filter == "system")
            url += "&$filter=ismanaged eq true";

        var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var roles = new List<SecurityRoleInfo>();
        foreach (var item in json.GetProperty("value").EnumerateArray())
        {
            roles.Add(new SecurityRoleInfo(
                item.GetProperty("roleid").GetGuid(),
                item.GetProperty("name").GetString() ?? string.Empty,
                item.GetProperty("ismanaged").GetBoolean(),
                item.TryGetProperty("iscustomizable", out var cust) && cust.ValueKind == JsonValueKind.Object
                    ? cust.GetProperty("Value").GetBoolean()
                    : true,
                item.TryGetProperty("_businessunitid_value", out var buId) && buId.ValueKind == JsonValueKind.String
                    ? Guid.Parse(buId.GetString()!)
                    : Guid.Empty,
                0)); // Privilege count not available in list query
        }

        return roles.AsReadOnly();
    }

    public async Task<SecurityRoleDetails> GetSecurityRoleDetailsAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting security role details for {RoleId}", roleId);
        var roleUrl = $"api/data/v9.2/roles({roleId})?$select=roleid,name,ismanaged";
        var roleResponse = await _http.GetAsync(roleUrl, cancellationToken);
        roleResponse.EnsureSuccessStatusCode();
        var roleJson = await roleResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var privUrl = $"api/data/v9.2/roles({roleId})/roleprivileges_association?$select=privilegeid,accessright,privilegedepthmask";
        var privResponse = await _http.GetAsync(privUrl, cancellationToken);
        privResponse.EnsureSuccessStatusCode();
        var privJson = await privResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var privileges = new List<SecurityPrivilege>();
        foreach (var priv in privJson.GetProperty("value").EnumerateArray())
        {
            var depthMask = priv.TryGetProperty("privilegedepthmask", out var dm) ? dm.GetInt32() : 0;
            privileges.Add(new SecurityPrivilege(
                priv.TryGetProperty("entityname", out var en) ? en.GetString() ?? string.Empty : string.Empty,
                priv.TryGetProperty("accessright", out var ar) ? ar.GetString() ?? string.Empty : string.Empty,
                DepthMaskToString(depthMask)));
        }

        return new SecurityRoleDetails(
            roleJson.GetProperty("roleid").GetGuid(),
            roleJson.GetProperty("name").GetString() ?? string.Empty,
            roleJson.GetProperty("ismanaged").GetBoolean(),
            privileges.AsReadOnly());
    }

    public async Task<Guid> CreateSecurityRoleAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating security role");
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/roles", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Headers.TryGetValues("OData-EntityId", out var values))
        {
            var entityIdUrl = values.First();
            var guidString = entityIdUrl[(entityIdUrl.LastIndexOf('(') + 1)..entityIdUrl.LastIndexOf(')')];
            return Guid.Parse(guidString);
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return result.GetProperty("roleid").GetGuid();
    }

    public async Task<IReadOnlyList<FieldSecurityProfileInfo>> GetFieldSecurityProfilesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting field security profiles");
        var url = "api/data/v9.2/fieldsecurityprofiles?$select=fieldsecurityprofileid,name";
        var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var profiles = new List<FieldSecurityProfileInfo>();
        foreach (var item in json.GetProperty("value").EnumerateArray())
        {
            profiles.Add(new FieldSecurityProfileInfo(
                item.GetProperty("fieldsecurityprofileid").GetGuid(),
                item.GetProperty("name").GetString() ?? string.Empty,
                [])); // Field permissions require a separate expand query
        }

        return profiles.AsReadOnly();
    }

    public async Task<Guid> CreateFieldSecurityProfileAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating field security profile");
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("api/data/v9.2/fieldsecurityprofiles", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Headers.TryGetValues("OData-EntityId", out var values))
        {
            var entityIdUrl = values.First();
            var guidString = entityIdUrl[(entityIdUrl.LastIndexOf('(') + 1)..entityIdUrl.LastIndexOf(')')];
            return Guid.Parse(guidString);
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return result.GetProperty("fieldsecurityprofileid").GetGuid();
    }

    private static string DepthMaskToString(int depthMask) => depthMask switch
    {
        1 => "User",
        2 => "BusinessUnit",
        4 => "ParentChildBU",
        8 => "Organization",
        _ => "None"
    };

    public async Task<BatchOperationResult> CreateRecordsBatchAsync(string entitySetName, IReadOnlyList<string> recordPayloadsJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating {Count} records in batch for {EntitySet}", recordPayloadsJson.Count, entitySetName);

        var createdIds = new List<Guid>();
        var errors = new List<string>();

        // Create records individually (Dataverse $batch is complex; individual POSTs are reliable)
        foreach (var payloadJson in recordPayloadsJson)
        {
            try
            {
                var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"api/data/v9.2/{entitySetName}", content, cancellationToken);
                response.EnsureSuccessStatusCode();

                if (response.Headers.TryGetValues("OData-EntityId", out var values))
                {
                    var entityIdUrl = values.First();
                    var guidString = entityIdUrl[(entityIdUrl.LastIndexOf('(') + 1)..entityIdUrl.LastIndexOf(')')];
                    createdIds.Add(Guid.Parse(guidString));
                }
                else
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                    if (result.TryGetProperty($"{entitySetName.TrimEnd('s')}id", out var idProp))
                    {
                        createdIds.Add(idProp.GetGuid());
                    }
                    else
                    {
                        createdIds.Add(Guid.NewGuid()); // Fallback ID
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to create record: {ex.Message}");
            }
        }

        return new BatchOperationResult(
            SuccessCount: createdIds.Count,
            FailureCount: errors.Count,
            CreatedRecordIds: createdIds.AsReadOnly(),
            Errors: errors.AsReadOnly());
    }

    public async Task<int> GetRecordCountAsync(string entitySetName, string? filter = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting record count for {EntitySet}", entitySetName);

        var url = $"api/data/v9.2/{entitySetName}?$count=true&$top=0";
        if (!string.IsNullOrWhiteSpace(filter))
        {
            url += $"&$filter={Uri.EscapeDataString(filter)}";
        }

        var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (json.TryGetProperty("@odata.count", out var countProp))
        {
            return countProp.GetInt32();
        }

        return 0;
    }

    public async Task<int> DeleteRecordsBatchAsync(string entitySetName, IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting {Count} records from {EntitySet}", recordIds.Count, entitySetName);

        var deletedCount = 0;
        foreach (var recordId in recordIds)
        {
            try
            {
                var response = await _http.DeleteAsync($"api/data/v9.2/{entitySetName}({recordId})", cancellationToken);
                response.EnsureSuccessStatusCode();
                deletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete record {RecordId} from {EntitySet}", recordId, entitySetName);
            }
        }

        return deletedCount;
    }

    // ─── DELETE OPERATIONS (for undo support) ───

    public async Task DeleteTableAsync(Guid metadataId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting table EntityDefinitions({MetadataId})", metadataId);
        var response = await _http.DeleteAsync($"api/data/v9.2/EntityDefinitions({metadataId})", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteFieldAsync(Guid tableMetadataId, Guid fieldMetadataId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting field {FieldId} from table {TableId}", fieldMetadataId, tableMetadataId);
        var response = await _http.DeleteAsync($"api/data/v9.2/EntityDefinitions({tableMetadataId})/Attributes({fieldMetadataId})", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteRelationshipAsync(Guid metadataId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting relationship {MetadataId}", metadataId);
        var response = await _http.DeleteAsync($"api/data/v9.2/RelationshipDefinitions({metadataId})", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteFormAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting form {FormId}", formId);
        var response = await _http.DeleteAsync($"api/data/v9.2/systemforms({formId})", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteViewAsync(Guid viewId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting view {ViewId}", viewId);
        var response = await _http.DeleteAsync($"api/data/v9.2/savedqueries({viewId})", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteDashboardAsync(Guid dashboardId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting dashboard {DashboardId}", dashboardId);
        var response = await _http.DeleteAsync($"api/data/v9.2/systemforms({dashboardId})", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteSecurityRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting security role {RoleId}", roleId);
        var response = await _http.DeleteAsync($"api/data/v9.2/roles({roleId})", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting workflow {WorkflowId}", workflowId);
        var response = await _http.DeleteAsync($"api/data/v9.2/workflows({workflowId})", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
