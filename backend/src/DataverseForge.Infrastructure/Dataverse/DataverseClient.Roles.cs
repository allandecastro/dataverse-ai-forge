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
    // Security roles (Dataverse "role" entity). Phase 5 of the MDA rollout —
    // ships read tools shared across UIBuilder + SolutionArchitect (and
    // future Security Builder), plus the M:N writes for assigning /
    // unassigning roles to model-driven apps. Role CRUD itself
    // (create / update / delete) is intentionally NOT in this phase — that's
    // the Security Builder's territory once it ships.
    //
    // Reference:
    //   role: https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/role
    //   appmoduleroles_association: M:N nav property between appmodule and role.

    private const string RoleSelect =
        "roleid,name,description,_businessunitid_value,ismanaged,"
        + "isautoassigned,appliesto,roletemplateid,modifiedon";

    public async Task<IReadOnlyList<SecurityRoleInfo>> GetSecurityRolesAsync(
        string? searchTerm = null,
        Guid? businessUnitId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Listing security roles (searchTerm={SearchTerm}, businessUnitId={BusinessUnitId})",
            searchTerm ?? "<all>",
            businessUnitId?.ToString() ?? "<all>");

        // Build $filter: search term + BU filter, no managed/unmanaged filter
        // (we want both — most useful OOB roles are managed).
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            // OData contains() function with single-quote escaping. The
            // searchTerm comes from the agent so we sanitize quote chars
            // by doubling them per OData spec.
            var escaped = searchTerm.Trim().Replace("'", "''");
            filters.Add($"contains(name,'{escaped}')");
        }
        if (businessUnitId is not null && businessUnitId.Value != Guid.Empty)
        {
            filters.Add($"_businessunitid_value eq {businessUnitId.Value}");
        }

        var url = $"{ApiBase}roles?$select={RoleSelect}&$orderby=name";
        if (filters.Count > 0)
        {
            url += $"&$filter={Uri.EscapeDataString(string.Join(" and ", filters))}";
        }

        // Need the Prefer header so the response carries
        // _businessunitid_value@OData.Community.Display.V1.FormattedValue
        // — that's how we surface the BU display name without an extra
        // $expand call.
        var listRequest = new HttpRequestMessage(HttpMethod.Get, url);
        AddFormattedValueAnnotationsHeader(listRequest);
        var response = await _http.SendAsync(listRequest, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "List security roles", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<ODataResponse<RoleListRow>>(cancellationToken);
        if (payload is null) return Array.Empty<SecurityRoleInfo>();

        return payload.Value
            .Select(MapRoleInfo)
            .ToList()
            .AsReadOnly();
    }

    public async Task<SecurityRoleDetails?> GetSecurityRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reading security role {RoleId}", roleId);

        var url = $"{ApiBase}roles({roleId})?$select={RoleSelect},_parentroleid_value,iscustomizable";
        var detailRequest = new HttpRequestMessage(HttpMethod.Get, url);
        AddFormattedValueAnnotationsHeader(detailRequest);
        var response = await _http.SendAsync(detailRequest, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSurfaceAsync(response, $"Read security role ({roleId})", cancellationToken);

        var row = await response.Content.ReadFromJsonAsync<RoleDetailRow>(cancellationToken);
        if (row is null) return null;

        // iscustomizable is a ManagedProperty in Dataverse — the Web API
        // serializes it as a nested object with a Value boolean. Default to
        // true if anything's off in the deserialize so we don't false-block
        // the agent on a metadata quirk.
        var isCustomizable = row.IsCustomizable?.Value ?? true;

        return new SecurityRoleDetails(
            Info: MapRoleInfo(row),
            ParentRoleId: string.IsNullOrWhiteSpace(row.ParentRoleId) ? null : Guid.Parse(row.ParentRoleId!),
            IsCustomizable: isCustomizable);
    }

    public async Task AssignAppRoleAsync(
        Guid appId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Assigning role {RoleId} to app {AppId}", roleId, appId);

        // M:N $ref form: POST a reference to the related entity into the
        // collection-valued nav property. Dataverse silently accepts the
        // re-association of an already-linked role (idempotent), which is
        // the behavior we want for the agent.
        //
        // The @odata.id reference MUST be an ABSOLUTE URL — Dataverse rejects
        // relative paths for $ref bodies with a generic "payload error".
        // Real session 2026-05-07 burned 1 round trip on a relative-path
        // form. Microsoft Learn ref:
        //   https://learn.microsoft.com/power-apps/developer/data-platform/webapi/associate-disassociate-entities-using-web-api
        var url = $"{ApiBase}appmodules({appId})/appmoduleroles_association/$ref";
        var body = new Dictionary<string, object>
        {
            ["@odata.id"] = $"{BuildAbsoluteApiBase()}roles({roleId})",
        };
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response,
            $"Assign role {roleId} to app {appId}", cancellationToken);
    }

    // BuildAbsoluteApiBase is now in the parent DataverseClient.cs partial
    // (any future @odata.id $ref builder needs it; not role-specific).

    public async Task UnassignAppRoleAsync(
        Guid appId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Unassigning role {RoleId} from app {AppId}", roleId, appId);

        // The DELETE form for an M:N $ref includes the related entity's id
        // in the URL itself — query string ?$id=… is the OData v4 spec, but
        // Dataverse also accepts the direct path form which is shorter and
        // less error-prone.
        var url = $"{ApiBase}appmodules({appId})/appmoduleroles_association({roleId})/$ref";
        var response = await _http.DeleteAsync(url, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response,
            $"Unassign role {roleId} from app {appId}", cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static SecurityRoleInfo MapRoleInfo(RoleListRow r) => new(
        RoleId: r.RoleId,
        Name: r.Name ?? string.Empty,
        Description: r.Description,
        BusinessUnitId: string.IsNullOrWhiteSpace(r.BusinessUnitId) ? null : Guid.Parse(r.BusinessUnitId!),
        BusinessUnitName: r.BusinessUnitName,
        IsManaged: r.IsManaged ?? false,
        IsAutoAssigned: r.IsAutoAssigned == 1,                  // picklist: 0=No, 1=Yes
        AppliesTo: r.AppliesTo,
        RoleTemplateId: string.IsNullOrWhiteSpace(r.RoleTemplateId) ? null : Guid.Parse(r.RoleTemplateId!),
        ModifiedOn: r.ModifiedOn);

    private class RoleListRow
    {
        [JsonPropertyName("roleid")] public Guid RoleId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }

        // Lookup: raw value + formatted-value annotation. The annotation
        // is auto-included by the `Prefer: odata.include-annotations` header
        // configured on the HttpClient — gives us the BU display name without
        // a separate $expand call.
        [JsonPropertyName("_businessunitid_value")] public string? BusinessUnitId { get; set; }
        [JsonPropertyName("_businessunitid_value@OData.Community.Display.V1.FormattedValue")]
        public string? BusinessUnitName { get; set; }

        [JsonPropertyName("ismanaged")] public bool? IsManaged { get; set; }
        [JsonPropertyName("isautoassigned")] public int? IsAutoAssigned { get; set; }
        [JsonPropertyName("appliesto")] public string? AppliesTo { get; set; }
        [JsonPropertyName("roletemplateid")] public string? RoleTemplateId { get; set; }
        [JsonPropertyName("modifiedon")] public DateTime? ModifiedOn { get; set; }
    }

    private sealed class RoleDetailRow : RoleListRow
    {
        [JsonPropertyName("_parentroleid_value")] public string? ParentRoleId { get; set; }
        [JsonPropertyName("iscustomizable")] public ManagedPropertyBool? IsCustomizable { get; set; }
    }

    /// <summary>
    /// Wire shape for Dataverse <c>ManagedProperty</c> bool columns
    /// (e.g. <c>iscustomizable</c>) — emitted as a nested object
    /// <c>{ Value: true, CanBeChanged: false, ManagedPropertyLogicalName: "..." }</c>.
    /// We only care about <c>Value</c>; the other fields are admin-side metadata.
    /// </summary>
    private sealed class ManagedPropertyBool
    {
        [JsonPropertyName("Value")] public bool Value { get; set; }
    }
}
