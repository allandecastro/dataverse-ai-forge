using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed partial class DataverseClient
{
    public async Task<IReadOnlyList<TableInfo>> GetTablesListAsync(
        string filter = "all",
        string? solutionName = null,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying entity definitions with filter={Filter}, solutionName={SolutionName}, searchTerm={SearchTerm}",
            filter, solutionName, searchTerm);

        var baseUrl = $"{ApiBase}EntityDefinitions?$select=LogicalName,DisplayName,SchemaName,MetadataId,IsCustomEntity,IsManaged,OwnershipType,PrimaryIdAttribute,PrimaryNameAttribute,HasNotes,HasActivities,ChangeTrackingEnabled";

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

                // EntityDefinitions does NOT expose a SolutionUniqueName property — the path
                // `Microsoft.Dynamics.CRM.SolutionComponentDefinition/SolutionUniqueName` returns 400.
                // The correct path: solution → solutioncomponents (componenttype 1 = Entity) → objectid
                // is the EntityDefinition's MetadataId. Fetch those, then filter EntityDefinitions
                // by MetadataId in.
                var escapedSolutionName = solutionName.Replace("'", "''");
                var solResp = await _http.GetFromJsonAsync<ODataResponse<SolutionResponse>>(
                    $"{ApiBase}solutions?$filter=uniquename eq '{escapedSolutionName}'&$select=solutionid",
                    cancellationToken);
                var solutionId = solResp?.Value.FirstOrDefault()?.SolutionId;
                if (solutionId is null || solutionId == Guid.Empty)
                {
                    _logger.LogDebug("Solution '{SolutionName}' not found", solutionName);
                    return new List<TableInfo>().AsReadOnly();
                }

                var compResp = await _http.GetFromJsonAsync<ODataResponse<SolutionComponentRef>>(
                    $"{ApiBase}solutioncomponents?$filter=componenttype eq 1 and _solutionid_value eq {solutionId.Value}&$select=objectid",
                    cancellationToken);
                var entityIds = compResp?.Value.Select(c => c.ObjectId).Where(id => id != Guid.Empty).ToList() ?? [];
                if (entityIds.Count == 0)
                {
                    _logger.LogDebug("Solution '{SolutionName}' has no entity components", solutionName);
                    return new List<TableInfo>().AsReadOnly();
                }

                // EntityDefinitions doesn't support `MetadataId in (...)`, but it does support
                // `MetadataId eq <guid> or MetadataId eq <guid>` chained with `or`.
                var idFilter = string.Join(" or ", entityIds.Select(id => $"MetadataId eq {id}"));
                filters.Add($"({idFilter})");
                break;
            case "all":
                break;
            default:
                throw new ArgumentException($"Invalid filter value: {filter}. Expected: all, custom, oob, solution", nameof(filter));
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            // Dataverse metadata queries return 501 Not Implemented for `contains()` —
            // even against real string properties like LogicalName / SchemaName on some
            // environments. `startswith()` is consistently supported. We pair it with a
            // client-side substring pass below so a searchTerm in the middle still
            // matches (e.g. searchTerm="gift" matching logical name "eppc_gift").
            var escapedTerm = searchTerm.Replace("'", "''");
            filters.Add($"(startswith(LogicalName,'{escapedTerm}') or startswith(SchemaName,'{escapedTerm}'))");
        }

        if (filters.Count > 0)
        {
            baseUrl += "&$filter=" + string.Join(" and ", filters);
        }

        IReadOnlyList<EntityDefinitionResponse> entities;
        try
        {
            var response = await _http.GetFromJsonAsync<ODataResponse<EntityDefinitionResponse>>(
                baseUrl, cancellationToken)
                ?? throw new InvalidOperationException("EntityDefinitions query returned null");
            entities = response.Value;
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.NotImplemented &&
            !string.IsNullOrWhiteSpace(searchTerm))
        {
            // Some Dataverse environments return 501 even on `startswith` over
            // metadata. Fall back to fetching *unfiltered* (still scoped by the
            // solution / custom / oob filters) and substring-matching client-side.
            _logger.LogInformation("EntityDefinitions search returned 501; retrying without searchTerm and filtering client-side");
            var fallbackFilters = filters.Where(f => !f.Contains("startswith(LogicalName")).ToList();
            var fallbackUrl = $"{ApiBase}EntityDefinitions?$select=LogicalName,DisplayName,SchemaName,MetadataId,IsCustomEntity,IsManaged,OwnershipType,PrimaryIdAttribute,PrimaryNameAttribute,HasNotes,HasActivities,ChangeTrackingEnabled";
            if (fallbackFilters.Count > 0)
                fallbackUrl += "&$filter=" + string.Join(" and ", fallbackFilters);
            var response = await _http.GetFromJsonAsync<ODataResponse<EntityDefinitionResponse>>(
                fallbackUrl, cancellationToken)
                ?? throw new InvalidOperationException("EntityDefinitions fallback query returned null");
            entities = response.Value;
        }

        _logger.LogDebug("Found {Count} entities", entities.Count);

        var result = entities
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
                ChangeTrackingEnabled: e.ChangeTrackingEnabled));

        // Client-side refinement: server filtered with startswith() (or skipped
        // filtering entirely on 501 fallback). Apply a true substring match here
        // so searchTerm="gift" matches LogicalName="eppc_gift" / DisplayName.
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(t =>
                t.LogicalName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                t.SchemaName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (t.DisplayName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return result.ToList().AsReadOnly();
    }

    private sealed class SolutionComponentRef
    {
        [JsonPropertyName("objectid")]
        public Guid ObjectId { get; set; }
    }

    public async Task<TableDetails> GetTableDetailsAsync(string tableLogicalName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying entity details for {TableLogicalName}", tableLogicalName);

        // NOTE: do NOT $select MaxLength on Attributes here. MaxLength only exists on
        // StringAttributeMetadata; requesting it from the heterogeneous Attributes
        // collection produces 400 Bad Request as soon as the table has even a single
        // non-string field. Fetch only base AttributeMetadata properties.
        var url = $"{ApiBase}EntityDefinitions(LogicalName='{tableLogicalName.ToLowerInvariant()}')"
            + "?$expand="
            + "Attributes($select=LogicalName,DisplayName,AttributeTypeName,IsCustomAttribute,RequiredLevel,IsSearchable,IsSecured),"
            + "Keys($select=LogicalName,DisplayName,KeyAttributes),"
            + "OneToManyRelationships($select=SchemaName,ReferencedEntity,ReferencedAttribute,ReferencingEntity,ReferencingAttribute,ReferencingEntityNavigationPropertyName),"
            + "ManyToOneRelationships($select=SchemaName,ReferencedEntity,ReferencedAttribute,ReferencingEntity,ReferencingAttribute,ReferencingEntityNavigationPropertyName)";

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
                IsSearchable: a.IsSearchable,
                IsSecured: a.IsSecured))
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
                ReferencingAttribute: r.ReferencingAttribute,
                ReferencingEntityNavigationPropertyName: r.ReferencingEntityNavigationPropertyName));
        }

        foreach (var r in entity.ManyToOneRelationships ?? [])
        {
            relationships.Add(new RelationshipInfo(
                SchemaName: r.SchemaName,
                RelationshipType: "ManyToOne",
                ReferencedEntity: r.ReferencedEntity,
                ReferencedAttribute: r.ReferencedAttribute,
                ReferencingEntity: r.ReferencingEntity,
                ReferencingAttribute: r.ReferencingAttribute,
                ReferencingEntityNavigationPropertyName: r.ReferencingEntityNavigationPropertyName));
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
            PrimaryIdAttribute: entity.PrimaryIdAttribute ?? string.Empty,
            PrimaryNameAttribute: entity.PrimaryNameAttribute ?? string.Empty,
            Columns: columns,
            Relationships: relationships.AsReadOnly(),
            AlternateKeys: alternateKeys);
    }

    public async Task<Guid> CreateTableAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating table via EntityDefinitions");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}EntityDefinitions", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create table (EntityDefinitions)", cancellationToken);

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<Guid> CreateFieldAsync(Guid tableMetadataId, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating field on table {TableMetadataId}", tableMetadataId);

        var url = $"{ApiBase}EntityDefinitions({tableMetadataId})/Attributes";
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Create field on table ({tableMetadataId})", cancellationToken);

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<Guid> CreateOneToManyRelationshipAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating 1:N relationship via RelationshipDefinitions");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}RelationshipDefinitions", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create 1:N relationship", cancellationToken);

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<Guid> CreateManyToManyRelationshipAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating N:N relationship via RelationshipDefinitions");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}RelationshipDefinitions", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create N:N relationship", cancellationToken);

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<bool> CanManyToManyAsync(string entityName, CancellationToken cancellationToken = default)
    {
        // CanManyToMany is an unbound action — POST a body with EntityName,
        // get back CanManyToManyResponse with a single CanBeRelated boolean.
        // Source: https://learn.microsoft.com/en-us/dynamics365/customer-engagement/web-api/canmanytomany
        var t = (entityName ?? string.Empty).ToLowerInvariant();
        _logger.LogDebug("CanManyToMany check for {Entity}", t);

        var requestBody = JsonSerializer.Serialize(new Dictionary<string, object> { ["EntityName"] = t });
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}CanManyToMany", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"CanManyToMany('{t}')", cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body)) return false;
        using var doc = JsonDocument.Parse(body);
        // Response field name is CanBeRelated per the SDK reference. Fall back
        // to true if the field isn't present (safer to attempt the relationship
        // creation than to wrongly reject — Dataverse will give a clear 400 if
        // the table really can't participate).
        if (doc.RootElement.TryGetProperty("CanBeRelated", out var canBeRelated)
            && canBeRelated.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return canBeRelated.GetBoolean();
        }
        return true;
    }

    public async Task<Guid> CreatePolymorphicLookupAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        // Unbound action — returns CreatePolymorphicLookupAttributeResponse with
        // AttributeId in the JSON body (NOT the OData-EntityId header that
        // metadata POSTs return). The action atomically creates the lookup column
        // plus N×OneToMany relationships, one per target table.
        _logger.LogInformation("Creating polymorphic lookup via CreatePolymorphicLookupAttribute. Payload: {Payload}", webApiPayloadJson);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}CreatePolymorphicLookupAttribute", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create polymorphic lookup", cancellationToken);

        return await ExtractAttributeIdFromBodyAsync(response, cancellationToken);
    }

    public async Task<Guid> CreateCustomerLookupAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        // Unbound action — same shape as CreatePolymorphicLookupAttribute but
        // hard-locked to Account+Contact target tables. Returns
        // CreateCustomerRelationshipsResponse with AttributeId in the body.
        _logger.LogInformation("Creating customer lookup via CreateCustomerRelationships. Payload: {Payload}", webApiPayloadJson);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}CreateCustomerRelationships", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create customer lookup", cancellationToken);

        return await ExtractAttributeIdFromBodyAsync(response, cancellationToken);
    }

    private static async Task<Guid> ExtractAttributeIdFromBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body)) return Guid.Empty;
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("AttributeId", out var attrId)
            && attrId.ValueKind == JsonValueKind.String
            && Guid.TryParse(attrId.GetString(), out var g))
        {
            return g;
        }
        return Guid.Empty;
    }

    public async Task<Guid> CreateGlobalOptionSetAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        // Log the FULL outgoing payload at Information level. Past pattern: option-set
        // 400s with no actionable detail because the agent's textual report ("unreadable
        // payload") loses the specifics. Info-level request logging + EnsureSuccessOrSurfaceAsync
        // response surfacing keeps the backend log diagnosable; correlate by timestamp
        // when reading both sides of the wire.
        _logger.LogInformation("Creating global option set via GlobalOptionSetDefinitions. Payload: {Payload}", webApiPayloadJson);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}GlobalOptionSetDefinitions", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create global option set (GlobalOptionSetDefinitions)", cancellationToken);

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task DeleteGlobalOptionSetAsync(Guid metadataId, CancellationToken cancellationToken = default)
    {
        // DELETE on GlobalOptionSetDefinitions cascades any Choice columns
        // referencing the option set — Dataverse handles the dependency walk
        // server-side. Used by UndoLastOperationTool to reverse a recent
        // ExecuteCreateGlobalOptionSet (F-25). Source: Microsoft Learn —
        // "Customize global option sets using the Web API".
        _logger.LogInformation("Deleting global option set {MetadataId} via GlobalOptionSetDefinitions", metadataId);

        var response = await _http.DeleteAsync($"{ApiBase}GlobalOptionSetDefinitions({metadataId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"DeleteGlobalOptionSet ({metadataId})", cancellationToken);
    }

    public async Task InsertOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Inserting option value via InsertOptionValue");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}InsertOptionValue", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "InsertOptionValue", cancellationToken);
    }

    public async Task InsertStatusValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Inserting status value via InsertStatusValue");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}InsertStatusValue", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "InsertStatusValue", cancellationToken);
    }

    public async Task UpdateOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating option value via UpdateOptionValue");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}UpdateOptionValue", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "UpdateOptionValue", cancellationToken);
    }

    public async Task DeleteOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting option value via DeleteOptionValue");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}DeleteOptionValue", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "DeleteOptionValue", cancellationToken);
    }

    public async Task<IReadOnlyList<OptionInfo>> GetLocalOptionSetOptionsAsync(
        string tableLogicalName, string fieldLogicalName, CancellationToken cancellationToken = default)
    {
        // The OData cast type required to expand the OptionSet differs by
        // attribute kind. Statuscode/statecode have their own subtypes; the
        // remaining picklist-shaped fields can be either PicklistAttributeMetadata
        // (single-select) or MultiSelectPicklistAttributeMetadata (multi-select).
        // We don't know which up-front, so we try Picklist first and fall back to
        // MultiSelectPicklist on 404. Past failure: hard-coding Picklist meant
        // multi-select picklists like `eppc_tags` returned an empty option list,
        // and the agent told the user "no option set found" when it really existed.
        var lower = fieldLogicalName.ToLowerInvariant();
        if (lower is "statuscode")
        {
            return await TryFetchOptionsAsync(tableLogicalName, lower,
                "Microsoft.Dynamics.CRM.StatusAttributeMetadata", includeStateMapping: true, cancellationToken);
        }
        if (lower is "statecode")
        {
            return await TryFetchOptionsAsync(tableLogicalName, lower,
                "Microsoft.Dynamics.CRM.StateAttributeMetadata", includeStateMapping: false, cancellationToken);
        }
        // Picklist first.
        var asPicklist = await TryFetchOptionsAsync(tableLogicalName, lower,
            "Microsoft.Dynamics.CRM.PicklistAttributeMetadata", includeStateMapping: false, cancellationToken);
        if (asPicklist.Count > 0) return asPicklist;
        // Fall through to MultiSelectPicklist.
        return await TryFetchOptionsAsync(tableLogicalName, lower,
            "Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata", includeStateMapping: false, cancellationToken);
    }

    private async Task<IReadOnlyList<OptionInfo>> TryFetchOptionsAsync(
        string tableLogicalName, string fieldLogicalName, string castType,
        bool includeStateMapping, CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}EntityDefinitions(LogicalName='{tableLogicalName.ToLowerInvariant()}')"
            + $"/Attributes(LogicalName='{fieldLogicalName}')/{castType}?$expand=OptionSet";

        try
        {
            var doc = await _http.GetFromJsonAsync<JsonElement>(url, cancellationToken);
            return ParseOptions(doc, includeStateMapping);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TryFetchOptions({Cast}) failed for {Table}.{Field}",
                castType, tableLogicalName, fieldLogicalName);
            return Array.Empty<OptionInfo>();
        }
    }

    public async Task<IReadOnlyList<OptionInfo>> GetGlobalOptionSetOptionsAsync(
        string optionSetName, CancellationToken cancellationToken = default)
    {
        var url = $"{ApiBase}GlobalOptionSetDefinitions(Name='{optionSetName.ToLowerInvariant()}')";

        try
        {
            var doc = await _http.GetFromJsonAsync<JsonElement>(url, cancellationToken);
            return ParseOptions(doc, includeStateMapping: false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetGlobalOptionSetOptions failed for {OptionSet} — returning empty", optionSetName);
            return Array.Empty<OptionInfo>();
        }
    }

    private static List<OptionInfo> ParseOptions(JsonElement doc, bool includeStateMapping)
    {
        // For local cast: doc has shape { OptionSet: { Options: [...] } }
        // For global: doc itself has Options at the top level.
        JsonElement optionsArray;
        if (doc.TryGetProperty("OptionSet", out var optionSetElem) &&
            optionSetElem.TryGetProperty("Options", out var inner))
        {
            optionsArray = inner;
        }
        else if (doc.TryGetProperty("Options", out var top))
        {
            optionsArray = top;
        }
        else
        {
            return [];
        }

        var result = new List<OptionInfo>();
        if (optionsArray.ValueKind != JsonValueKind.Array) return result;

        foreach (var opt in optionsArray.EnumerateArray())
        {
            var value = opt.TryGetProperty("Value", out var v) && v.TryGetInt32(out var vi) ? vi : 0;
            var label = string.Empty;
            if (opt.TryGetProperty("Label", out var lbl) &&
                lbl.TryGetProperty("UserLocalizedLabel", out var ull) &&
                ull.TryGetProperty("Label", out var ullLabel))
            {
                label = ullLabel.GetString() ?? string.Empty;
            }
            int? stateValue = null;
            if (includeStateMapping &&
                opt.TryGetProperty("State", out var stateElem) &&
                stateElem.TryGetInt32(out var sv))
            {
                stateValue = sv;
            }
            // Color is optional on every option metadata type — null when the
            // option has no chip color set in the maker portal.
            string? color = null;
            if (opt.TryGetProperty("Color", out var colorElem) &&
                colorElem.ValueKind == JsonValueKind.String)
            {
                var c = colorElem.GetString();
                if (!string.IsNullOrWhiteSpace(c)) color = c;
            }
            result.Add(new OptionInfo(value, label, stateValue, color));
        }
        return result;
    }

    public async Task<IReadOnlyList<GlobalOptionSetSummary>> GetGlobalOptionSetsAsync(
        string? searchTerm = null, bool customOnly = false, CancellationToken cancellationToken = default)
    {
        // GlobalOptionSetDefinitions returns a heterogeneous collection (picklist + boolean).
        // Cast to OptionSetMetadata so we can $select Options safely.
        var url = $"{ApiBase}GlobalOptionSetDefinitions/Microsoft.Dynamics.CRM.OptionSetMetadata"
            + "?$select=Name,DisplayName,IsCustomOptionSet,IsManaged,Options";

        try
        {
            var doc = await _http.GetFromJsonAsync<JsonElement>(url, cancellationToken);
            if (!doc.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<GlobalOptionSetSummary>();
            var lowered = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm.ToLowerInvariant();

            foreach (var os in arr.EnumerateArray())
            {
                var name = os.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var isCustom = os.TryGetProperty("IsCustomOptionSet", out var ic) && ic.ValueKind == JsonValueKind.True;
                var isManaged = os.TryGetProperty("IsManaged", out var im) && im.ValueKind == JsonValueKind.True;

                if (customOnly && !isCustom) continue;

                string displayName = string.Empty;
                if (os.TryGetProperty("DisplayName", out var dn) &&
                    dn.TryGetProperty("UserLocalizedLabel", out var ull) &&
                    ull.TryGetProperty("Label", out var lbl))
                {
                    displayName = lbl.GetString() ?? string.Empty;
                }

                if (lowered is not null
                    && !name.Contains(lowered, StringComparison.OrdinalIgnoreCase)
                    && !displayName.Contains(lowered, StringComparison.OrdinalIgnoreCase))
                {
                    // Also check option labels — useful for "do we have a Status choice?" type queries.
                    var matchInOption = false;
                    if (os.TryGetProperty("Options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var opt in opts.EnumerateArray())
                        {
                            if (opt.TryGetProperty("Label", out var optLbl) &&
                                optLbl.TryGetProperty("UserLocalizedLabel", out var optUll) &&
                                optUll.TryGetProperty("Label", out var optLabel))
                            {
                                var s = optLabel.GetString() ?? string.Empty;
                                if (s.Contains(lowered, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchInOption = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (!matchInOption) continue;
                }

                var options = ParseOptions(os, includeStateMapping: false);
                results.Add(new GlobalOptionSetSummary(
                    Name: name,
                    DisplayName: displayName,
                    IsCustom: isCustom,
                    IsManaged: isManaged,
                    Options: options.AsReadOnly()));
            }

            return results.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetGlobalOptionSets failed — returning empty");
            return [];
        }
    }

    /// <summary>
    /// Resolve a global option set's MetadataId by Name or DisplayName. Used by
    /// PlanCreateFieldTool to embed a real GUID in the GlobalOptionSet@odata.bind
    /// URL. Past Dataverse versions silently rejected the (Name='x') form with
    /// "Guid should contain 32 digits" — using the GUID is the reliable contract.
    /// </summary>
    public async Task<Guid?> ResolveGlobalOptionSetMetadataIdAsync(string nameOrDisplayName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nameOrDisplayName))
            return null;

        // Try exact Name match first via direct path lookup — cheapest and most precise.
        var loweredName = nameOrDisplayName.ToLowerInvariant();
        var byNameUrl = $"{ApiBase}GlobalOptionSetDefinitions(Name='{loweredName}')?$select=MetadataId";
        try
        {
            var direct = await _http.GetFromJsonAsync<JsonElement>(byNameUrl, cancellationToken);
            if (direct.TryGetProperty("MetadataId", out var midDirect)
                && midDirect.ValueKind == JsonValueKind.String
                && Guid.TryParse(midDirect.GetString(), out var idDirect))
            {
                return idDirect;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Direct Name='{Name}' lookup failed, falling back to enumeration", loweredName);
        }

        // Fallback: enumerate and match against Name or DisplayName (case-insensitive).
        var listUrl = $"{ApiBase}GlobalOptionSetDefinitions/Microsoft.Dynamics.CRM.OptionSetMetadata"
            + "?$select=MetadataId,Name,DisplayName";
        try
        {
            var doc = await _http.GetFromJsonAsync<JsonElement>(listUrl, cancellationToken);
            if (!doc.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var os in arr.EnumerateArray())
            {
                var name = os.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                string displayName = string.Empty;
                if (os.TryGetProperty("DisplayName", out var dn)
                    && dn.TryGetProperty("UserLocalizedLabel", out var ull)
                    && ull.TryGetProperty("Label", out var lbl))
                {
                    displayName = lbl.GetString() ?? string.Empty;
                }

                if (string.Equals(name, nameOrDisplayName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(displayName, nameOrDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    if (os.TryGetProperty("MetadataId", out var mid)
                        && mid.ValueKind == JsonValueKind.String
                        && Guid.TryParse(mid.GetString(), out var id))
                    {
                        return id;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate GlobalOptionSetDefinitions while resolving '{Name}'", nameOrDisplayName);
        }

        return null;
    }

    public async Task<Guid> CreateAlternateKeyAsync(string tableLogicalName, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        var t = tableLogicalName.ToLowerInvariant();
        _logger.LogInformation("Creating alternate key on table {Table}", t);

        var url = $"{ApiBase}EntityDefinitions(LogicalName='{t}')/Keys";
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Create alternate key on '{t}'", cancellationToken);

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task DeleteAlternateKeyAsync(string tableLogicalName, Guid keyMetadataId, CancellationToken cancellationToken = default)
    {
        var t = tableLogicalName.ToLowerInvariant();
        _logger.LogInformation("Deleting alternate key {KeyId} on table {Table}", keyMetadataId, t);

        // Per Dataverse Web API: keys are deleted by MetadataId GUID via the
        // table's Keys collection.
        var url = $"{ApiBase}EntityDefinitions(LogicalName='{t}')/Keys({keyMetadataId})";
        var response = await _http.DeleteAsync(url, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Delete alternate key on '{t}'", cancellationToken);
    }

    public async Task DeactivateAlternateKeyAsync(string tableLogicalName, string keyLogicalName, CancellationToken cancellationToken = default)
    {
        // DeactivateEntityKey is an unbound action — POST to /DeactivateEntityKey
        // with EntityLogicalName + EntityKeyLogicalName in the body. No return
        // value (HTTP 204). The key, its definition, and the underlying SQL
        // index stay; the uniqueness constraint stops being enforced.
        var t = tableLogicalName.ToLowerInvariant();
        var k = keyLogicalName.ToLowerInvariant();
        _logger.LogInformation("Deactivating alternate key {Key} on table {Table}", k, t);

        var requestBody = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["EntityLogicalName"] = t,
            ["EntityKeyLogicalName"] = k,
        });
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}DeactivateEntityKey", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"DeactivateEntityKey('{t}','{k}')", cancellationToken);
    }

    public async Task ReactivateAlternateKeyAsync(string tableLogicalName, string keyLogicalName, CancellationToken cancellationToken = default)
    {
        // ReactivateEntityKey is the symmetrical counterpart: same body shape,
        // /ReactivateEntityKey endpoint. Per the doc, it queues an async system
        // job to rebuild the index — the HTTP call returns immediately, but
        // the key isn't usable for Upsert until the index job completes.
        var t = tableLogicalName.ToLowerInvariant();
        var k = keyLogicalName.ToLowerInvariant();
        _logger.LogInformation("Reactivating alternate key {Key} on table {Table}", k, t);

        var requestBody = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["EntityLogicalName"] = t,
            ["EntityKeyLogicalName"] = k,
        });
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}ReactivateEntityKey", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"ReactivateEntityKey('{t}','{k}')", cancellationToken);
    }

    public async Task<IReadOnlyList<AlternateKeyInfo>> GetAlternateKeysAsync(string tableLogicalName, CancellationToken cancellationToken = default)
    {
        var t = tableLogicalName.ToLowerInvariant();
        _logger.LogDebug("Querying alternate keys for {Table}", t);

        var url = $"{ApiBase}EntityDefinitions(LogicalName='{t}')/Keys"
            + "?$select=MetadataId,LogicalName,DisplayName,KeyAttributes";

        var response = await _http.GetFromJsonAsync<ODataResponse<EntityKeyResponse>>(url, cancellationToken)
            ?? throw new InvalidOperationException($"Keys query for '{t}' returned null");

        return response.Value
            .Select(k => new AlternateKeyInfo(
                LogicalName: k.LogicalName ?? string.Empty,
                DisplayName: ExtractLabel(k.DisplayName),
                KeyAttributes: k.KeyAttributes ?? Array.Empty<string>(),
                MetadataId: k.MetadataId))
            .ToList()
            .AsReadOnly();
    }

    public async Task ModifyTableAsync(string tableLogicalName, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        var t = tableLogicalName.ToLowerInvariant();
        _logger.LogInformation("Modifying table {Table}", t);

        // PUT EntityDefinitions(LogicalName='x') with a Microsoft.Dynamics.CRM.EntityMetadata
        // body merges only the fields present in the payload. SchemaName/LogicalName must
        // match the existing table.
        // MSCRM.MergeLabels: 'true' tells Dataverse to merge label changes (DisplayName,
        // Description) instead of replacing the entire localization set — without it the
        // PUT clears unstated locale variants.
        var url = $"{ApiBase}EntityDefinitions(LogicalName='{t}')";
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        request.Headers.Add("MSCRM.MergeLabels", "true");
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Modify table '{t}'", cancellationToken);
    }

    public async Task ModifyFieldAsync(string tableLogicalName, string fieldLogicalName, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        // Logical names are the authoritative identifier — Dataverse Web API
        // accepts EntityDefinitions(LogicalName='x')/Attributes(LogicalName='y'),
        // so we never have to involve agent-supplied (and frequently hallucinated)
        // MetadataId GUIDs.
        var t = tableLogicalName.ToLowerInvariant();
        var f = fieldLogicalName.ToLowerInvariant();
        _logger.LogInformation("Modifying field {Field} on table {Table}", f, t);

        var url = $"{ApiBase}EntityDefinitions(LogicalName='{t}')/Attributes(LogicalName='{f}')";
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PutAsync(url, content, cancellationToken);

        await EnsureSuccessOrSurfaceAsync(response, $"Modify field '{f}' on '{t}'", cancellationToken);
    }

    public async Task<TableMetadata> GetTableMetadataAsync(string tableLogicalName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying table metadata for {TableLogicalName}", tableLogicalName);

        // Dataverse logical names are always lowercase. If the agent passes
        // mixed-case (e.g. "adc_Laptop") the lookup returns 404 and the verifier
        // wrongly reports NOT FOUND.
        var url = $"{ApiBase}EntityDefinitions(LogicalName='{tableLogicalName.ToLowerInvariant()}')"
            + "?$select=MetadataId,LogicalName,DisplayName,SchemaName,OwnershipType,HasNotes,HasActivities,"
            + "ChangeTrackingEnabled,IsAuditEnabled,IsActivity,IsActivityParty,IsLogicalEntity,IsRenameable";

        var entity = await _http.GetFromJsonAsync<TableMetadataResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException($"EntityDefinition for '{tableLogicalName}' returned null");

        return new TableMetadata(
            MetadataId: entity.MetadataId,
            LogicalName: entity.LogicalName,
            DisplayName: ExtractLabel(entity.DisplayName),
            SchemaName: entity.SchemaName,
            OwnershipType: entity.OwnershipType ?? string.Empty,
            HasNotes: entity.HasNotes,
            HasActivities: entity.HasActivities,
            ChangeTrackingEnabled: entity.ChangeTrackingEnabled,
            IsAuditEnabled: entity.IsAuditEnabled?.Value ?? false,
            IsActivity: entity.IsActivity,
            IsActivityParty: entity.IsActivityParty,
            IsLogicalEntity: entity.IsLogicalEntity,
            IsRenameable: entity.IsRenameable?.Value ?? true);
    }

    public async Task<FieldMetadata> GetFieldMetadataAsync(string tableLogicalName, string fieldLogicalName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying field metadata for {FieldLogicalName} on {TableLogicalName}", fieldLogicalName, tableLogicalName);

        // Logical names are always lowercase in Dataverse — defensive lower-casing
        // so verification doesn't false-FAIL when the agent passes mixed-case names.
        // NOTE: do NOT include MaxLength in this $select. MaxLength only exists on
        // StringAttributeMetadata, so requesting it from the base AttributeMetadata
        // returns 400 for any non-string field (DateTime, Lookup, Picklist, …) and
        // that has caused field verification to false-FAIL across the board.
        // To get MaxLength for string fields, cast the URL to the derived type.
        var loweredTable = tableLogicalName.ToLowerInvariant();
        var loweredField = fieldLogicalName.ToLowerInvariant();
        var url = $"{ApiBase}EntityDefinitions(LogicalName='{loweredTable}')"
            + $"/Attributes(LogicalName='{loweredField}')"
            + "?$select=MetadataId,LogicalName,DisplayName,AttributeTypeName,RequiredLevel,IsSearchable,IsAuditEnabled,IsSecured";

        var attr = await _http.GetFromJsonAsync<FieldMetadataResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException($"Attribute '{fieldLogicalName}' on '{tableLogicalName}' returned null");

        // For string fields, do a follow-up cast call to read MaxLength. For everything
        // else MaxLength is meaningless and the value stays null.
        int? maxLength = null;
        var typeName = attr.AttributeTypeName?.Value ?? string.Empty;
        if (string.Equals(typeName, "StringType", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeName, "MemoType", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var castType = string.Equals(typeName, "MemoType", StringComparison.OrdinalIgnoreCase)
                    ? "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
                    : "Microsoft.Dynamics.CRM.StringAttributeMetadata";
                var castUrl = $"{ApiBase}EntityDefinitions(LogicalName='{loweredTable}')"
                    + $"/Attributes(LogicalName='{loweredField}')/{castType}"
                    + "?$select=MaxLength";
                var castResp = await _http.GetFromJsonAsync<FieldMetadataResponse>(castUrl, cancellationToken);
                maxLength = castResp?.MaxLength;
            }
            catch (Exception ex)
            {
                // Best-effort — if the cast call fails we just leave maxLength null.
                _logger.LogDebug(ex, "Cast call to read MaxLength on {Table}.{Field} failed; continuing without it.",
                    loweredTable, loweredField);
            }
        }

        return new FieldMetadata(
            MetadataId: attr.MetadataId,
            LogicalName: attr.LogicalName,
            DisplayName: ExtractLabel(attr.DisplayName),
            AttributeType: typeName,
            RequiredLevel: attr.RequiredLevel?.Value ?? "None",
            MaxLength: maxLength,
            IsSearchable: attr.IsSearchable,
            IsAuditEnabled: attr.IsAuditEnabled?.Value ?? false,
            IsSecured: attr.IsSecured);
    }

    public async Task<RelationshipMetadata> GetRelationshipMetadataAsync(string relationshipSchemaName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying relationship metadata for {RelationshipSchemaName}", relationshipSchemaName);

        var url = $"{ApiBase}RelationshipDefinitions(SchemaName='{relationshipSchemaName}')"
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

    public async Task DeleteTableAsync(Guid metadataId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting table EntityDefinitions({MetadataId})", metadataId);
        var response = await _http.DeleteAsync($"{ApiBase}EntityDefinitions({metadataId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Delete table", cancellationToken);
    }

    public async Task DeleteFieldAsync(Guid tableMetadataId, Guid fieldMetadataId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting field {FieldId} from table {TableId}", fieldMetadataId, tableMetadataId);
        var response = await _http.DeleteAsync($"{ApiBase}EntityDefinitions({tableMetadataId})/Attributes({fieldMetadataId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Delete field", cancellationToken);
    }

    public async Task DeleteFieldByLogicalNameAsync(string tableLogicalName, string fieldLogicalName, CancellationToken cancellationToken = default)
    {
        // Logical names are authoritative; use them directly so we never have to
        // ask the agent for MetadataId GUIDs.
        var t = tableLogicalName.ToLowerInvariant();
        var f = fieldLogicalName.ToLowerInvariant();
        _logger.LogInformation("Deleting field {Field} from table {Table} (by logical name)", f, t);
        var response = await _http.DeleteAsync($"{ApiBase}EntityDefinitions(LogicalName='{t}')/Attributes(LogicalName='{f}')", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Delete field '{f}' on '{t}'", cancellationToken);
    }

    public async Task DeleteRelationshipAsync(Guid metadataId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting relationship {MetadataId}", metadataId);
        var response = await _http.DeleteAsync($"{ApiBase}RelationshipDefinitions({metadataId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Delete relationship", cancellationToken);
    }
}
