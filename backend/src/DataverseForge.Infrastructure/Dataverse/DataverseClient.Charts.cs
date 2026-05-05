using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed partial class DataverseClient
{
    // Charts (Dataverse "savedqueryvisualization" entity). Org-owned only — we
    // never write to userqueryvisualization. The two XML blobs we care about
    // are `datadescription` (FetchXML + category/measure mapping) and
    // `presentationdescription` (Microsoft Chart Controls XAML).

    public async Task<IReadOnlyList<ChartInfo>> GetChartsListAsync(
        string tableLogicalName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying charts for {TableLogicalName}", tableLogicalName);

        var t = tableLogicalName.ToLowerInvariant();
        var url = $"{ApiBase}savedqueryvisualizations"
            + $"?$filter=primaryentitytypecode eq '{t}'"
            + "&$select=savedqueryvisualizationid,name,description,primaryentitytypecode,isdefault,ismanaged";

        var response = await _http.GetFromJsonAsync<ODataResponse<SavedQueryVisualizationResponse>>(url, cancellationToken)
            ?? throw new InvalidOperationException($"Charts query for '{tableLogicalName}' returned null");

        return response.Value
            .Select(c => new ChartInfo(
                c.SavedQueryVisualizationId,
                c.Name ?? string.Empty,
                c.Description ?? string.Empty,
                c.PrimaryEntityTypeCode ?? string.Empty,
                c.IsDefault,
                c.IsManaged))
            .ToList()
            .AsReadOnly();
    }

    public async Task<ChartDetails> GetChartDetailsAsync(Guid chartId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying chart details for {ChartId}", chartId);

        var url = $"{ApiBase}savedqueryvisualizations({chartId})"
            + "?$select=savedqueryvisualizationid,name,description,primaryentitytypecode,"
            + "datadescription,presentationdescription,isdefault,ismanaged";

        var chart = await _http.GetFromJsonAsync<SavedQueryVisualizationResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException($"Chart '{chartId}' returned null");

        return new ChartDetails(
            chart.SavedQueryVisualizationId,
            chart.Name ?? string.Empty,
            chart.Description ?? string.Empty,
            chart.PrimaryEntityTypeCode ?? string.Empty,
            chart.DataDescription ?? string.Empty,
            chart.PresentationDescription ?? string.Empty,
            chart.IsDefault,
            chart.IsManaged);
    }

    public async Task<Guid> CreateChartAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating chart, payload={Length} chars", webApiPayloadJson.Length);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}savedqueryvisualizations", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create chart (savedqueryvisualization)", cancellationToken);

        // Same pattern as record creation: prefer the OData-EntityId header,
        // fall back to body parsing.
        if (response.Headers.TryGetValues("OData-EntityId", out var values))
        {
            var entityIdUrl = values.First();
            var guidString = entityIdUrl[(entityIdUrl.LastIndexOf('(') + 1)..entityIdUrl.LastIndexOf(')')];
            if (Guid.TryParse(guidString, out var headerGuid))
                return headerGuid;
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (body.TryGetProperty("savedqueryvisualizationid", out var idProp))
            return idProp.GetGuid();

        throw new InvalidOperationException("Chart created but Dataverse returned no id");
    }

    public async Task UpdateChartAsync(Guid chartId, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating chart {ChartId}, payload={Length} chars", chartId, webApiPayloadJson.Length);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{ApiBase}savedqueryvisualizations({chartId})") { Content = content };
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Update chart ({chartId})", cancellationToken);
    }

    public async Task DeleteChartAsync(Guid chartId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting chart {ChartId}", chartId);

        var response = await _http.DeleteAsync($"{ApiBase}savedqueryvisualizations({chartId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Delete chart ({chartId})", cancellationToken);
    }

    /// <summary>
    /// Wire-format DTO for savedqueryvisualization rows. Mirrors the OData
    /// $select fields we request — keep both lists in sync.
    /// </summary>
    private sealed class SavedQueryVisualizationResponse
    {
        [JsonPropertyName("savedqueryvisualizationid")]
        public Guid SavedQueryVisualizationId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("primaryentitytypecode")]
        public string? PrimaryEntityTypeCode { get; set; }

        [JsonPropertyName("datadescription")]
        public string? DataDescription { get; set; }

        [JsonPropertyName("presentationdescription")]
        public string? PresentationDescription { get; set; }

        [JsonPropertyName("isdefault")]
        public bool IsDefault { get; set; }

        [JsonPropertyName("ismanaged")]
        public bool IsManaged { get; set; }
    }
}
