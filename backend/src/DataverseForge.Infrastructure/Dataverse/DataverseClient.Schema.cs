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
            + "Attributes($select=LogicalName,DisplayName,AttributeTypeName,IsCustomAttribute,RequiredLevel,IsSearchable),"
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

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("CreateTable failed: {Status} - sent={Payload} - response={Body}",
                (int)response.StatusCode, webApiPayloadJson, body);
            throw new HttpRequestException($"Dataverse returned {(int)response.StatusCode}: {body}");
        }

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<Guid> CreateFieldAsync(Guid tableMetadataId, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating field on table {TableMetadataId}", tableMetadataId);

        var url = $"{ApiBase}EntityDefinitions({tableMetadataId})/Attributes";
        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("CreateField failed: {Status} - sent={Payload} - response={Body}",
                (int)response.StatusCode, webApiPayloadJson, body);
            throw new HttpRequestException($"Dataverse returned {(int)response.StatusCode}: {body}");
        }

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

    public async Task<Guid> CreateGlobalOptionSetAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        // Log the FULL outgoing payload at Information level. Past pattern: option-set
        // 400s with no actionable detail because the agent's textual report ("unreadable
        // payload") loses the specifics. Logging both sides of the wire here lets us
        // diagnose a recurring bug from the backend stdout without having to add tracing.
        _logger.LogInformation("Creating global option set via GlobalOptionSetDefinitions. Payload: {Payload}", webApiPayloadJson);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}GlobalOptionSetDefinitions", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("CreateGlobalOptionSet FAILED. Status={Status} Sent={Payload} Response={Body}",
                (int)response.StatusCode, webApiPayloadJson, body);
            // EnsureSuccessOrSurfaceAsync needs a fresh response — re-throw with the body.
            var detail = ExtractDataverseErrorMessage(body) ?? Truncate(body, 800);
            throw new HttpRequestException(
                $"Create global option set failed: HTTP {(int)response.StatusCode} — {detail}",
                inner: null,
                statusCode: response.StatusCode);
        }

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task InsertOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Inserting option value via InsertOptionValue");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}InsertOptionValue", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("InsertOptionValue failed: {Status} - sent={Payload} - response={Body}",
                (int)response.StatusCode, webApiPayloadJson, body);
            throw new HttpRequestException($"Dataverse returned {(int)response.StatusCode}: {body}");
        }
    }

    public async Task InsertStatusValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Inserting status value via InsertStatusValue");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}InsertStatusValue", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("InsertStatusValue failed: {Status} - sent={Payload} - response={Body}",
                (int)response.StatusCode, webApiPayloadJson, body);
            throw new HttpRequestException($"Dataverse returned {(int)response.StatusCode}: {body}");
        }
    }

    public async Task UpdateOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating option value via UpdateOptionValue");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}UpdateOptionValue", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("UpdateOptionValue failed: {Status} - sent={Payload} - response={Body}",
                (int)response.StatusCode, webApiPayloadJson, body);
            throw new HttpRequestException($"Dataverse returned {(int)response.StatusCode}: {body}");
        }
    }

    public async Task DeleteOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting option value via DeleteOptionValue");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}DeleteOptionValue", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("DeleteOptionValue failed: {Status} - sent={Payload} - response={Body}",
                (int)response.StatusCode, webApiPayloadJson, body);
            throw new HttpRequestException($"Dataverse returned {(int)response.StatusCode}: {body}");
        }
    }

    public async Task<IReadOnlyList<OptionInfo>> GetLocalOptionSetOptionsAsync(
        string tableLogicalName, string fieldLogicalName, CancellationToken cancellationToken = default)
    {
        // Try Picklist first; if 404 fall back to Status (statuscode is StatusAttributeMetadata,
        // statecode is StateAttributeMetadata, all others are PicklistAttributeMetadata).
        var lower = fieldLogicalName.ToLowerInvariant();
        var castType = lower switch
        {
            "statuscode" => "Microsoft.Dynamics.CRM.StatusAttributeMetadata",
            "statecode" => "Microsoft.Dynamics.CRM.StateAttributeMetadata",
            _ => "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        };

        var url = $"{ApiBase}EntityDefinitions(LogicalName='{tableLogicalName.ToLowerInvariant()}')"
            + $"/Attributes(LogicalName='{lower}')/{castType}?$expand=OptionSet";

        try
        {
            var doc = await _http.GetFromJsonAsync<JsonElement>(url, cancellationToken);
            return ParseOptions(doc, includeStateMapping: lower == "statuscode");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetLocalOptionSetOptions failed for {Table}.{Field} — returning empty",
                tableLogicalName, fieldLogicalName);
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
            result.Add(new OptionInfo(value, label, stateValue));
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
            + "?$select=MetadataId,LogicalName,DisplayName,SchemaName,OwnershipType,HasNotes,HasActivities,ChangeTrackingEnabled,IsAuditEnabled";

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
            IsAuditEnabled: entity.IsAuditEnabled?.Value ?? false);
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
            + "?$select=LogicalName,DisplayName,AttributeTypeName,RequiredLevel,IsSearchable,IsAuditEnabled";

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
            LogicalName: attr.LogicalName,
            DisplayName: ExtractLabel(attr.DisplayName),
            AttributeType: typeName,
            RequiredLevel: attr.RequiredLevel?.Value ?? "None",
            MaxLength: maxLength,
            IsSearchable: attr.IsSearchable,
            IsAuditEnabled: attr.IsAuditEnabled?.Value ?? false);
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

    public async Task DeleteRelationshipAsync(Guid metadataId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting relationship {MetadataId}", metadataId);
        var response = await _http.DeleteAsync($"{ApiBase}RelationshipDefinitions({metadataId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Delete relationship", cancellationToken);
    }
}
