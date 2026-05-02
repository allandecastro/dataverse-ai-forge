using System.Net.Http.Json;
using System.Text;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed partial class DataverseClient
{
    public async Task<IReadOnlyList<FormInfo>> GetFormsListAsync(string tableLogicalName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying forms for {TableLogicalName}", tableLogicalName);

        var url = $"{ApiBase}systemforms?$filter=objecttypecode eq '{tableLogicalName}'&$select=formid,name,type,description,isdefault";
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

        var url = $"{ApiBase}systemforms({formId})?$select=formid,name,type,formxml";
        var form = await _http.GetFromJsonAsync<SystemFormResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException($"Form '{formId}' returned null");

        return new FormDefinition(form.FormId, form.Name, MapFormType(form.Type), form.FormXml ?? string.Empty);
    }

    public async Task<Guid> CreateFormAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating form via systemforms");

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}systemforms", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create form", cancellationToken);

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task UpdateFormAsync(Guid formId, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        // PATCH systemforms({formId}) with the same OData payload shape as create.
        // Dataverse merges only the fields present in the payload; pass a payload
        // containing just `formxml` to update the layout, or include name/description
        // to also rename. The publisher of the form is preserved.
        _logger.LogInformation("Updating form {FormId} via systemforms PATCH", formId);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{ApiBase}systemforms({formId})")
        {
            Content = content
        };
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Update form {formId}", cancellationToken);
    }

    public async Task<IReadOnlyList<ViewInfo>> GetViewsListAsync(string tableLogicalName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying views for {TableLogicalName}", tableLogicalName);

        var url = $"{ApiBase}savedqueries?$filter=returnedtypecode eq '{tableLogicalName}'&$select=savedqueryid,name,description,fetchxml,layoutxml";
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
        var response = await _http.PostAsync($"{ApiBase}savedqueries", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create view", cancellationToken);

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task UpdateViewAsync(Guid viewId, string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        // PATCH savedqueries({viewId}) merges only fields present in payload.
        // Pass `name`/`description`/`fetchxml`/`layoutxml` to update those.
        // `returnedtypecode` is immutable on existing records — never include it.
        _logger.LogInformation("Updating view {ViewId} via savedqueries PATCH", viewId);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{ApiBase}savedqueries({viewId})")
        {
            Content = content
        };
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Update view {viewId}", cancellationToken);
    }

    private static string MapFormType(int type) => type switch
    {
        2 => "main",
        5 => "quickCreate",
        6 => "quickView",
        0 => "dashboard",
        _ => $"type_{type}"
    };

    public async Task DeleteFormAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting form {FormId}", formId);
        var response = await _http.DeleteAsync($"{ApiBase}systemforms({formId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Delete form", cancellationToken);
    }

    public async Task DeleteViewAsync(Guid viewId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting view {ViewId}", viewId);
        var response = await _http.DeleteAsync($"{ApiBase}savedqueries({viewId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Delete view", cancellationToken);
    }
}
