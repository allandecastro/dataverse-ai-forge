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
    // Workflow CRUD + activation for the AutomationBuilder agent.
    // Activation goes via PATCH on statecode+statuscode (see ActivateWorkflowAsync) —
    // direct PATCH of statecode alone gets rejected by Dataverse.

    private const string WorkflowSelect =
        "workflowid,name,description,primaryentity,category,statecode,statuscode,"
        + "ismanaged,iscustomizable,scope,modifiedon,clientdata,xaml";

    public async Task<IReadOnlyList<WorkflowInfo>> GetWorkflowsAsync(
        WorkflowCategory? categoryFilter = null,
        string? primaryEntityLogicalName = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Listing workflows (category={Category}, primaryEntity={PrimaryEntity})",
            categoryFilter?.ToString() ?? "<all>",
            primaryEntityLogicalName ?? "<all>");

        var filters = new List<string>();
        if (categoryFilter is not null)
        {
            filters.Add($"category eq {(int)categoryFilter}");
        }
        else
        {
            // Default to BPF + Business Rule only — out-of-scope categories are filtered.
            filters.Add("(category eq 2 or category eq 4)");
        }
        if (!string.IsNullOrWhiteSpace(primaryEntityLogicalName))
        {
            var lower = primaryEntityLogicalName.ToLowerInvariant();
            filters.Add($"primaryentity eq '{lower}'");
        }

        // List omits the heavy XAML/clientdata blobs — they're only fetched on single reads.
        var url = $"{ApiBase}workflows"
            + "?$select=workflowid,name,description,primaryentity,category,statecode,"
            + "ismanaged,iscustomizable,scope,modifiedon"
            + "&$filter=" + Uri.EscapeDataString(string.Join(" and ", filters))
            + "&$orderby=name";

        var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "List workflows", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<ODataResponse<WorkflowListRow>>(cancellationToken);
        if (payload is null) return Array.Empty<WorkflowInfo>();

        return payload.Value
            .Select(MapWorkflowInfo)
            .ToList()
            .AsReadOnly();
    }

    public async Task<WorkflowInfo?> GetWorkflowAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reading workflow {WorkflowId}", workflowId);

        var url = $"{ApiBase}workflows({workflowId})?$select={WorkflowSelect}";
        var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSurfaceAsync(response,
            $"Read workflow ({workflowId})", cancellationToken);

        var row = await response.Content.ReadFromJsonAsync<WorkflowListRow>(cancellationToken);
        return row is null ? null : MapWorkflowInfo(row);
    }

    public async Task<Guid> CreateWorkflowAsync(
        string webApiPayloadJson,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating workflow with payload length {Length}", webApiPayloadJson.Length);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}workflows", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create workflow", cancellationToken);

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task UpdateWorkflowAsync(
        Guid workflowId,
        string webApiPayloadJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webApiPayloadJson) || webApiPayloadJson.Trim() == "{}")
        {
            _logger.LogDebug("UpdateWorkflow: empty payload — skipping");
            return;
        }
        _logger.LogDebug("PATCH /workflows({WorkflowId}) payload length {Length}", workflowId, webApiPayloadJson.Length);

        // Strip statecode/statuscode — those go via Activate/Deactivate, not PATCH.
        var sanitized = StripStateFields(webApiPayloadJson);

        var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{ApiBase}workflows({workflowId})")
        {
            Content = new StringContent(sanitized, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Update workflow {workflowId}", cancellationToken);
    }

    public async Task DeleteWorkflowAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DELETE /workflows({WorkflowId})", workflowId);
        var response = await _http.DeleteAsync($"{ApiBase}workflows({workflowId})", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("DeleteWorkflow: {WorkflowId} already absent — treating as no-op", workflowId);
            return;
        }
        await EnsureSuccessOrSurfaceAsync(response, $"Delete workflow {workflowId}", cancellationToken);
    }

    public async Task ActivateWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        await SetWorkflowStateAsync(workflowId, stateCode: 1, statusCode: 2, "Activate", cancellationToken);
    }

    public async Task DeactivateWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        await SetWorkflowStateAsync(workflowId, stateCode: 0, statusCode: 1, "Deactivate", cancellationToken);
    }

    // Dataverse requires statecode + statuscode together. (1,2) = activated; (0,1) = draft.
    private async Task SetWorkflowStateAsync(
        Guid workflowId,
        int stateCode,
        int statusCode,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Operation} workflow {WorkflowId} (state={State}, status={Status})",
            operationLabel, workflowId, stateCode, statusCode);

        var body = new Dictionary<string, object>
        {
            ["statecode"] = stateCode,
            ["statuscode"] = statusCode,
        };
        var json = JsonSerializer.Serialize(body);
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{ApiBase}workflows({workflowId})")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response,
            $"{operationLabel} workflow {workflowId}", cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static WorkflowInfo MapWorkflowInfo(WorkflowListRow r) => new(
        WorkflowId: r.WorkflowId,
        Name: r.Name ?? string.Empty,
        Description: r.Description,
        Category: r.Category switch
        {
            2 => WorkflowCategory.BusinessRule,
            4 => WorkflowCategory.BusinessProcessFlow,
            _ => (WorkflowCategory)r.Category,
        },
        PrimaryEntityLogicalName: r.PrimaryEntity ?? string.Empty,
        IsActivated: r.StateCode == 1,
        IsManaged: r.IsManaged ?? false,
        IsCustomizable: r.IsCustomizable?.Value ?? true,
        Scope: r.Scope ?? 0,
        ModifiedOn: r.ModifiedOn);

    private static string StripStateFields(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return payloadJson;
            var dict = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("statecode") || prop.NameEquals("statuscode")) continue;
                dict[prop.Name] = JsonHelpers.JsonElementToObject(prop.Value);
            }
            return JsonSerializer.Serialize(dict);
        }
        catch (JsonException)
        {
            return payloadJson;
        }
    }

    private sealed class WorkflowListRow
    {
        [JsonPropertyName("workflowid")] public Guid WorkflowId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("primaryentity")] public string? PrimaryEntity { get; set; }
        [JsonPropertyName("category")] public int Category { get; set; }
        [JsonPropertyName("statecode")] public int StateCode { get; set; }
        [JsonPropertyName("statuscode")] public int StatusCode { get; set; }
        [JsonPropertyName("ismanaged")] public bool? IsManaged { get; set; }
        [JsonPropertyName("iscustomizable")] public WorkflowManagedPropertyBool? IsCustomizable { get; set; }
        [JsonPropertyName("scope")] public int? Scope { get; set; }
        [JsonPropertyName("modifiedon")] public DateTime? ModifiedOn { get; set; }
        [JsonPropertyName("clientdata")] public string? ClientData { get; set; }
        [JsonPropertyName("xaml")] public string? Xaml { get; set; }
    }

    private sealed class WorkflowManagedPropertyBool
    {
        [JsonPropertyName("Value")] public bool Value { get; set; }
    }

    private static class JsonHelpers
    {
        public static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToArray(),
            JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            _ => null,
        };
    }
}
