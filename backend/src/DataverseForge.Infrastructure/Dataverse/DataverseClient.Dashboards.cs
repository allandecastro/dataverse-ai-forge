using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed partial class DataverseClient
{
    // Dashboards (Dataverse "systemform" entity with type=0). Org-owned only —
    // we never write to userform. Same /systemforms endpoint as forms; the
    // type filter is what distinguishes a dashboard from a Main / QC / QV form.

    public async Task<IReadOnlyList<DashboardInfo>> GetDashboardsListAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying dashboards (systemform with type=0)");

        // type=0 → Dashboard. Default Dataverse may return ~30+ rows including
        // the OOB dashboards; the agent filters / picks the one it cares about.
        var url = $"{ApiBase}systemforms"
            + "?$filter=type eq 0"
            + "&$select=formid,name,description,isdefault,ismanaged"
            + "&$orderby=name";

        var response = await _http.GetFromJsonAsync<ODataResponse<SystemFormDashboardResponse>>(url, cancellationToken)
            ?? throw new InvalidOperationException("Dashboards query returned null");

        return response.Value
            .Select(d => new DashboardInfo(
                d.FormId,
                d.Name ?? string.Empty,
                d.Description ?? string.Empty,
                d.IsDefault,
                d.IsManaged))
            .ToList()
            .AsReadOnly();
    }

    public async Task<DashboardDefinition> GetDashboardDefinitionAsync(Guid dashboardId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying dashboard definition for {DashboardId}", dashboardId);

        var url = $"{ApiBase}systemforms({dashboardId})"
            + "?$select=formid,name,description,formxml,isdefault,ismanaged";

        var dash = await _http.GetFromJsonAsync<SystemFormDashboardResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException($"Dashboard '{dashboardId}' returned null");

        return new DashboardDefinition(
            dash.FormId,
            dash.Name ?? string.Empty,
            dash.Description ?? string.Empty,
            dash.FormXml ?? string.Empty,
            dash.IsDefault,
            dash.IsManaged);
    }

    public async Task<Guid> CreateDashboardAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating dashboard, payload={Length} chars", webApiPayloadJson.Length);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}systemforms", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create dashboard (systemform type=0)", cancellationToken);

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task UpdateDashboardAsync(Guid dashboardId, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating dashboard {DashboardId}, payload={Length} chars", dashboardId, webApiPayloadJson.Length);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{ApiBase}systemforms({dashboardId})") { Content = content };
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Update dashboard ({dashboardId})", cancellationToken);
    }

    public async Task DeleteDashboardAsync(Guid dashboardId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting dashboard {DashboardId}", dashboardId);

        var response = await _http.DeleteAsync($"{ApiBase}systemforms({dashboardId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Delete dashboard ({dashboardId})", cancellationToken);
    }

    /// <summary>
    /// Wire-format DTO for systemform rows when treating them as dashboards.
    /// Reuses the same /systemforms endpoint that <see cref="GetFormsListAsync"/>
    /// hits but with the dashboard column projection — kept separate to avoid
    /// the form DTO bloating with FormXml on every list call.
    /// </summary>
    private sealed class SystemFormDashboardResponse
    {
        [JsonPropertyName("formid")]
        public Guid FormId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("formxml")]
        public string? FormXml { get; set; }

        [JsonPropertyName("isdefault")]
        public bool IsDefault { get; set; }

        [JsonPropertyName("ismanaged")]
        public bool IsManaged { get; set; }
    }
}
