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
    // Workflow CRUD + activation for the Automation Builder agent. v1 owns
    // Business Process Flows (category=4) + Business Rules (category=2);
    // classic workflows / dialogs / actions / modern flows round-trip
    // through reads but stay out of scope for plan/execute (see
    // UNSUPPORTED.md).
    //
    // References:
    //   workflow:                https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/workflow
    //   SetStateRequest pattern: deprecated SOAP-action style; in the Web
    //                            API it's a PATCH against /workflows({id})
    //                            with statecode + statuscode set
    //                            atomically (see ActivateWorkflowAsync).

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
            // Default: BPF + Business Rule only. Classic workflows (0),
            // dialogs (1), actions (3), modern flows (5) are out of scope
            // for the AB agent. Caller can pass categoryFilter=null AND
            // patch the URL to bypass this if they really need everything.
            filters.Add("(category eq 2 or category eq 4)");
        }
        if (!string.IsNullOrWhiteSpace(primaryEntityLogicalName))
        {
            var lower = primaryEntityLogicalName.ToLowerInvariant();
            filters.Add($"primaryentity eq '{lower}'");
        }

        // Select WITHOUT the heavy XAML/clientdata blobs on the list path
        // — agents only need them on the single-workflow read.
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

        // Dataverse rejects PATCH of statecode/statuscode on workflows —
        // activation lives on Activate/Deactivate. Strip them defensively
        // before sending so a planner mistake doesn't fail mid-request.
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

    /// <summary>
    /// PATCH the statecode + statuscode in a single payload. Dataverse
    /// requires both together — patching statecode alone gets a 400
    /// "must include statuscode" response. (1, 2) = activated;
    /// (0, 1) = draft. Other statuscodes exist but aren't user-facing.
    /// </summary>
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
            // Out-of-scope categories still round-trip the read; the agent
            // refuses to plan against them. Map to BusinessRule as a safe
            // default since the read tools surface every workflow.
            _ => (WorkflowCategory)r.Category,
        },
        PrimaryEntityLogicalName: r.PrimaryEntity ?? string.Empty,
        IsActivated: r.StateCode == 1,
        IsManaged: r.IsManaged ?? false,
        IsCustomizable: r.IsCustomizable?.Value ?? true,
        Scope: r.Scope ?? 0,
        ModifiedOn: r.ModifiedOn);

    /// <summary>
    /// Remove statecode/statuscode from an OData PATCH payload before
    /// sending. Dataverse rejects activation changes via PATCH on
    /// /workflows; they must go through the SetState path. Returns the
    /// original string when it doesn't parse as JSON (the EnsureSuccess
    /// downstream will surface that case clearly).
    /// </summary>
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

    /// <summary>
    /// Bridge helper — converts a <see cref="JsonElement"/> to a plain
    /// .NET object suitable for re-serialization through
    /// <see cref="JsonSerializer.Serialize{T}(T, JsonSerializerOptions)"/>.
    /// Lives here so <see cref="StripStateFields"/> can rebuild a clean
    /// payload without dragging the existing Records.cs helper across.
    /// </summary>
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
