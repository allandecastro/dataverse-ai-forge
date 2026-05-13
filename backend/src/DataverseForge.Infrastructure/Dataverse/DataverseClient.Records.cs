using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed partial class DataverseClient
{
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
                var response = await _http.PostAsync($"{ApiBase}{entitySetName}", content, cancellationToken);
                await EnsureSuccessOrSurfaceAsync(response, $"Create {entitySetName} record", cancellationToken);

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

        // Some Dataverse envs reject `$top=0` as "Invalid value for $top".
        // `$count=true` alone returns @odata.count with the first page of
        // records — we ignore the body and just read the count.
        var url = $"{ApiBase}{entitySetName}?$count=true";
        if (!string.IsNullOrWhiteSpace(filter))
        {
            url += $"&$filter={Uri.EscapeDataString(filter)}";
        }

        var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"GetRecordCount({entitySetName})", cancellationToken);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (json.TryGetProperty("@odata.count", out var countProp))
        {
            return countProp.GetInt32();
        }

        return 0;
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRecordsAsync(
        string entitySetName,
        string? select = null,
        string? filter = null,
        string? orderby = null,
        int top = 5,
        CancellationToken cancellationToken = default)
    {
        // Defensive clamp — the tool wrapper also clamps, but this method is on the
        // public client surface so callers from anywhere get a sane bound.
        if (top <= 0) top = 5;
        if (top > 50) top = 50;

        _logger.LogDebug("Getting up to {Top} records from {EntitySet}", top, entitySetName);

        var query = new List<string> { $"$top={top}" };
        if (!string.IsNullOrWhiteSpace(select)) query.Add($"$select={Uri.EscapeDataString(select)}");
        if (!string.IsNullOrWhiteSpace(filter)) query.Add($"$filter={Uri.EscapeDataString(filter)}");
        if (!string.IsNullOrWhiteSpace(orderby)) query.Add($"$orderby={Uri.EscapeDataString(orderby)}");

        var url = $"{ApiBase}{entitySetName}?{string.Join('&', query)}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddFormattedValueAnnotationsHeader(request);
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"GetRecords({entitySetName})", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (!envelope.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(arr.GetArrayLength());
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var prop in el.EnumerateObject())
            {
                row[prop.Name] = JsonElementToObject(prop.Value);
            }
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// Asks Dataverse to return formatted display values alongside raw lookup
    /// GUIDs and option-set integers. With this header the response carries
    /// extra <c>@OData.Community.Display.V1.FormattedValue</c> annotations:
    ///   <c>_ownerid_value</c> → "132d5dcd-…" (the GUID, as before)
    ///   <c>_ownerid_value@OData.Community.Display.V1.FormattedValue</c> → "Allan De Castro"
    ///   <c>statuscode</c> → 1
    ///   <c>statuscode@OData.Community.Display.V1.FormattedValue</c> → "Active"
    /// Without this, agents have no way to resolve "who is the owner?" from
    /// a contact fetch — the GUID alone forces a follow-up call to systemusers
    /// (and even then the agent has to know if the owner is a user or a team).
    /// Microsoft Learn ref:
    ///   https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/retrieve-related-records-through-single-valued-navigation-properties#formatted-values
    /// </summary>
    private static void AddFormattedValueAnnotationsHeader(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(
            "Prefer",
            "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");
    }

    /// <summary>
    /// Convert a JsonElement to a primitive .NET value for clean JSON re-serialization
    /// when the tool wrapper hands the dict back to the agent. Numbers prefer int → long
    /// → double, strings stay as strings, null/object/array fall through as-is.
    /// </summary>
    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt32(out var i) ? i
            : el.TryGetInt64(out var l) ? l
            : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Undefined => null,
        // For nested objects / arrays, hand back the raw JSON text — the agent sees it
        // and we don't have to model every shape Dataverse might return.
        _ => el.GetRawText()
    };

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RunFetchXmlAsync(
        string entitySetName,
        string fetchXml,
        int top = 50,
        CancellationToken cancellationToken = default)
    {
        if (top <= 0) top = 50;
        if (top > 500) top = 500;
        _logger.LogDebug("Running FetchXML against {EntitySet}, top={Top}", entitySetName, top);

        // Dataverse Web API exposes FetchXML execution as a query-string param
        // on the entity collection: GET /<entitysetname>?fetchXml=<encoded>.
        // For very large fetches the URL can exceed practical limits — Microsoft
        // recommends switching to POST in that case. We're well under the limit
        // for typical view fetches (~1-2 KB), so GET is fine for v1.
        var encoded = Uri.EscapeDataString(fetchXml);
        var url = $"{ApiBase}{entitySetName}?fetchXml={encoded}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddFormattedValueAnnotationsHeader(request);
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"RunFetchXml({entitySetName})", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (!envelope.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            if (rows.Count >= top) break;
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var prop in el.EnumerateObject())
            {
                row[prop.Name] = JsonElementToObject(prop.Value);
            }
            rows.Add(row);
        }
        return rows;
    }

    public async Task<SavedQueryRunResult> RunSavedQueryAsync(
        Guid savedQueryId,
        int top = 50,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Running savedquery {SavedQueryId}, top={Top}", savedQueryId, top);

        // Step 1 — pull the view's fetchxml + which entity it targets.
        var viewUrl = $"{ApiBase}savedqueries({savedQueryId})?$select=name,fetchxml,returnedtypecode";
        var viewResponse = await _http.GetAsync(viewUrl, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(viewResponse, $"RunSavedQuery: load view {savedQueryId}", cancellationToken);
        var view = await viewResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var viewName = view.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? string.Empty : string.Empty;
        var fetchXml = view.TryGetProperty("fetchxml", out var fProp) ? fProp.GetString() ?? string.Empty : string.Empty;
        var returnedTypeCode = view.TryGetProperty("returnedtypecode", out var rProp) ? rProp.GetString() ?? string.Empty : string.Empty;

        if (string.IsNullOrWhiteSpace(fetchXml))
        {
            throw new InvalidOperationException(
                $"Saved query '{savedQueryId}' returned no FetchXML — the view may be empty or require a query type other than 0 (public).");
        }

        // Step 2 — resolve the entity set name. EntityDefinitions is authoritative
        // and handles irregular plurals (e.g. eppc_quiz → eppc_quizzes) that a
        // naive +"s" pluralizer would miss.
        var entitySetName = await GetEntitySetNameAsync(returnedTypeCode, cancellationToken);

        // Step 3 — execute the fetch. Reuse the same path as the public method
        // so behavior stays consistent.
        var rows = await RunFetchXmlAsync(entitySetName, fetchXml, top, cancellationToken);

        return new SavedQueryRunResult(
            SavedQueryId: savedQueryId,
            ViewName: viewName,
            EntityLogicalName: returnedTypeCode,
            EntitySetName: entitySetName,
            FetchXml: fetchXml,
            RowCount: rows.Count,
            Rows: rows);
    }

    /// <summary>
    /// Authoritative entity-set-name lookup via the EntityDefinitions endpoint.
    /// Beats hand-rolled pluralization for irregular custom-table plurals
    /// (custom tables can have any EntitySetName the maker chose).
    /// </summary>
    private async Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
            throw new InvalidOperationException("Cannot resolve entity set name: entityLogicalName is empty.");

        var url = $"{ApiBase}EntityDefinitions(LogicalName='{entityLogicalName}')?$select=EntitySetName";
        var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"GetEntitySetName({entityLogicalName})", cancellationToken);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (doc.TryGetProperty("EntitySetName", out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var name = prop.GetString();
            if (!string.IsNullOrWhiteSpace(name)) return name!;
        }
        throw new InvalidOperationException(
            $"EntityDefinitions did not return EntitySetName for logical name '{entityLogicalName}'.");
    }

    public async Task<int> DeleteRecordsBatchAsync(string entitySetName, IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting {Count} records from {EntitySet}", recordIds.Count, entitySetName);

        var deletedCount = 0;
        foreach (var recordId in recordIds)
        {
            try
            {
                var response = await _http.DeleteAsync($"{ApiBase}{entitySetName}({recordId})", cancellationToken);
                await EnsureSuccessOrSurfaceAsync(response, $"Delete {entitySetName} record", cancellationToken);
                deletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete record {RecordId} from {EntitySet}", recordId, entitySetName);
            }
        }

        return deletedCount;
    }
}
