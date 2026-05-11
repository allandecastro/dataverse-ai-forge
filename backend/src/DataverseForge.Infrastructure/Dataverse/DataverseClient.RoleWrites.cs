using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed partial class DataverseClient
{
    // Role CRUD + privilege management (Phase 6 — Security Builder agent).
    // Reads (GetSecurityRoles, GetSecurityRole) and the M:N app↔role writes
    // already live in DataverseClient.Roles.cs; this partial only owns the
    // role-record CRUD + privilege-matrix surface that Security Builder
    // exclusively writes against.
    //
    // Commit 1 ships: Create + AddPrivileges + GetRolePrivileges +
    // GetBusinessUnits + GetPrivilegesForTable.
    // Commit 2 adds:  Update + RemovePrivilege.
    // Commit 3 adds:  Delete + CountUsersAndTeamsForRole.
    //
    // Reference docs:
    //   role:                https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/role
    //   AddPrivilegesRole:   https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/addprivilegesrole
    //   PrivilegeDepth enum: https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/role#BKMK_AccessRight
    //   privilegedepthmask:  bitmask emitted by /roleprivileges_association — 1=Basic, 2=Local, 4=Deep, 8=Global.

    public async Task<Guid> CreateRoleAsync(
        string webApiPayloadJson,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating security role with payload length {Length}", webApiPayloadJson.Length);

        var content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}roles", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Create security role", cancellationToken);

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task UpdateRoleAsync(
        Guid roleId,
        string webApiPayloadJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webApiPayloadJson) || webApiPayloadJson.Trim() == "{}")
        {
            _logger.LogDebug("UpdateRole: empty payload for role {RoleId} — skipping", roleId);
            return;
        }

        _logger.LogDebug("PATCH /roles({RoleId}) payload length {Length}", roleId, webApiPayloadJson.Length);

        // If-Match: * tells Dataverse "patch the row whatever its current
        // etag is" — without it the server treats the call as an upsert
        // (PATCH-or-create) which is wrong for an UPDATE. The bracketed
        // url form matches the role read path.
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{ApiBase}roles({roleId})")
        {
            Content = new StringContent(webApiPayloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"Update role {roleId}", cancellationToken);
    }

    public async Task AddPrivilegesToRoleAsync(
        Guid roleId,
        IReadOnlyList<RolePrivilegeAssignment> privileges,
        CancellationToken cancellationToken = default)
    {
        if (privileges is null || privileges.Count == 0)
        {
            _logger.LogDebug("AddPrivilegesToRole: empty privilege list for role {RoleId} — skipping", roleId);
            return;
        }

        _logger.LogDebug(
            "Adding {Count} privilege(s) to role {RoleId}", privileges.Count, roleId);

        // AddPrivilegesRole accepts a single Privileges array; Dataverse upserts
        // duplicates so a re-run with overlapping privileges is safe.
        var body = new
        {
            Privileges = privileges.Select(p => new Dictionary<string, object>
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.RolePrivilege",
                ["Depth"] = p.Depth,
                ["PrivilegeId"] = p.PrivilegeId,
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = $"{ApiBase}roles({roleId})/Microsoft.Dynamics.CRM.AddPrivilegesRole";
        var response = await _http.PostAsync(url, content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response,
            $"AddPrivilegesRole on role {roleId}", cancellationToken);
    }

    public async Task DeleteRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DELETE /roles({RoleId})", roleId);

        var response = await _http.DeleteAsync($"{ApiBase}roles({roleId})", cancellationToken);

        // Idempotent: 404 means the role was already gone — treat as success.
        // Matches the unassign / DeleteApp / DeleteSiteMap patterns in the
        // rest of the client. Teardown loops + retry paths benefit from
        // this contract.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("DeleteRole: role {RoleId} already absent — treating as no-op", roleId);
            return;
        }
        await EnsureSuccessOrSurfaceAsync(response, $"Delete role {roleId}", cancellationToken);
    }

    public async Task<int> CountUsersAndTeamsForRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Counting principals holding role {RoleId}", roleId);

        // OData $count=true on the join entities. systemuserroles + teamroles
        // are the two M:N join tables for role↔user and role↔team.
        // Microsoft Learn:
        //   https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/systemuserroles
        //   https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/teamroles
        var userCount = await CountAssociationAsync(
            $"{ApiBase}systemuserroles?$count=true&$filter=roleid eq {roleId}&$top=1",
            cancellationToken);
        var teamCount = await CountAssociationAsync(
            $"{ApiBase}teamroles?$count=true&$filter=roleid eq {roleId}&$top=1",
            cancellationToken);
        return userCount + teamCount;
    }

    private async Task<int> CountAssociationAsync(string url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Dataverse requires Prefer: odata.include-annotations="*" to surface
        // the @odata.count annotation in some envs; setting the same header
        // we use elsewhere for formatted-values is safe + consistent.
        request.Headers.TryAddWithoutValidation("Prefer", "odata.include-annotations=\"*\"");
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Count role associations", cancellationToken);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (doc.RootElement.TryGetProperty("@odata.count", out var c) && c.TryGetInt32(out var count))
        {
            return count;
        }
        // Fallback: enumerate Value array length (shouldn't be hit when
        // $count=true is honored, but covers older envs).
        if (doc.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array)
        {
            return v.GetArrayLength();
        }
        return 0;
    }

    public async Task RemovePrivilegeFromRoleAsync(
        Guid roleId,
        Guid privilegeId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Removing privilege {PrivilegeId} from role {RoleId}", privilegeId, roleId);

        // RemovePrivilegeRole takes ONE privilege per call (Dataverse limit) —
        // the caller loops in execute_update_security_role for multi-privilege
        // removals. Action signature: { "PrivilegeId": "<guid>" }.
        var body = new Dictionary<string, object> { ["PrivilegeId"] = privilegeId };
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = $"{ApiBase}roles({roleId})/Microsoft.Dynamics.CRM.RemovePrivilegeRole";
        var response = await _http.PostAsync(url, content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response,
            $"RemovePrivilegeRole on role {roleId} (privilege {privilegeId})", cancellationToken);
    }

    public async Task<IReadOnlyList<RolePrivilegeAssignment>> GetRolePrivilegesAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reading privileges attached to role {RoleId}", roleId);

        // privilegedepthmask is a bitmask: 1=Basic, 2=Local, 4=Deep, 8=Global.
        // The role-priv association can carry MORE than one bit (additive depth
        // — e.g. mask=3 means "Basic AND Local"), but for a freshly-applied
        // AddPrivilegesRole call only one bit is ever set. We surface the
        // HIGHEST bit so the verifier compares apples-to-apples with what the
        // planner asked for.
        var url = $"{ApiBase}roles({roleId})/roleprivileges_association"
            + "?$select=privilegeid,name";
        var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response,
            $"List privileges for role {roleId}", cancellationToken);

        // The role-priv association doesn't carry privilegedepthmask in its
        // entity-set projection (it lives on the join row, which is exposed
        // separately). Re-fetch via the explicit roleprivilegescollection
        // endpoint so we get the depth.
        var depthsByPrivilegeId = await ReadRolePrivilegeDepthsAsync(roleId, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<ODataResponse<PrivilegeRowMin>>(cancellationToken);
        if (payload is null) return Array.Empty<RolePrivilegeAssignment>();

        return payload.Value
            .Select(p =>
            {
                var depth = depthsByPrivilegeId.TryGetValue(p.PrivilegeId, out var d) ? d : "Basic";
                return new RolePrivilegeAssignment(p.PrivilegeId, depth, p.Name);
            })
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<BusinessUnitInfo>> GetBusinessUnitsAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing business units");

        var url = $"{ApiBase}businessunits"
            + "?$select=businessunitid,name,_parentbusinessunitid_value"
            + "&$orderby=name";
        var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "List business units", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<ODataResponse<BusinessUnitRow>>(cancellationToken);
        if (payload is null) return Array.Empty<BusinessUnitInfo>();

        return payload.Value
            .Select(b => new BusinessUnitInfo(
                BusinessUnitId: b.BusinessUnitId,
                Name: b.Name ?? string.Empty,
                ParentBusinessUnitId: string.IsNullOrWhiteSpace(b.ParentBusinessUnitId)
                    ? null
                    : Guid.Parse(b.ParentBusinessUnitId!)))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<RoleTemplateInfo>> GetRoleTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing role templates");

        var url = $"{ApiBase}roletemplates"
            + "?$select=roletemplateid,name,description"
            + "&$orderby=name";
        var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "List role templates", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<ODataResponse<RoleTemplateRow>>(cancellationToken);
        if (payload is null) return Array.Empty<RoleTemplateInfo>();

        return payload.Value
            .Select(t => new RoleTemplateInfo(
                RoleTemplateId: t.RoleTemplateId,
                Name: t.Name ?? string.Empty,
                Description: t.Description))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<PrivilegeMetadata>> GetPrivilegesForTableAsync(
        string tableLogicalName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tableLogicalName))
            return Array.Empty<PrivilegeMetadata>();

        var lowered = tableLogicalName.ToLowerInvariant();
        _logger.LogDebug("Listing privileges defined on table {LogicalName}", lowered);

        // EntityDefinitions(LogicalName='X')?$expand=Privileges only — Privileges
        // is the navigation that exposes the entity's privilege table, including
        // the four CanBe* depth flags + AccessRight enum.
        var url = $"{ApiBase}EntityDefinitions(LogicalName='{Uri.EscapeDataString(lowered)}')"
            + "?$select=LogicalName&$expand=Privileges";
        var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("GetPrivilegesForTable: table {LogicalName} not found", lowered);
            return Array.Empty<PrivilegeMetadata>();
        }
        await EnsureSuccessOrSurfaceAsync(response,
            $"Read privileges for table {lowered}", cancellationToken);

        var doc = await response.Content.ReadFromJsonAsync<EntityWithPrivileges>(cancellationToken);
        if (doc?.Privileges is null) return Array.Empty<PrivilegeMetadata>();

        return doc.Privileges
            .Select(MapPrivilege)
            .ToList()
            .AsReadOnly();
    }

    // ── helpers (Commit 1) ──────────────────────────────────────────────

    private async Task<Dictionary<Guid, string>> ReadRolePrivilegeDepthsAsync(
        Guid roleId, CancellationToken cancellationToken)
    {
        // Dataverse exposes the per-privilege depth on the join entity
        // /roleprivilegescollection (a non-standard collection-valued nav).
        // Fall back to the role's RetrieveRolePrivilegesRole action — that
        // returns the full set with depths, which is exactly what we need.
        // Both endpoints exist; the action form is more stable across versions.
        var url = $"{ApiBase}roles({roleId})/Microsoft.Dynamics.CRM.RetrieveRolePrivilegesRole";
        var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "RetrieveRolePrivilegesRole returned {Status}; depth lookup falls back to Basic",
                (int)response.StatusCode);
            return new Dictionary<Guid, string>();
        }

        var doc = await response.Content.ReadFromJsonAsync<RolePrivilegesActionResponse>(cancellationToken);
        if (doc?.RolePrivileges is null) return new Dictionary<Guid, string>();

        return doc.RolePrivileges.ToDictionary(
            r => r.PrivilegeId,
            r => MapDepthEnum(r.Depth));
    }

    /// <summary>
    /// Convert Dataverse's <c>PrivilegeDepth</c> enum string to the literal we
    /// send back through <c>AddPrivilegesRole</c>. The enum names match exactly
    /// across read (RetrieveRolePrivilegesRole) and write (AddPrivilegesRole)
    /// endpoints — Basic / Local / Deep / Global — so this is a defensive
    /// pass-through that also normalizes case. Unknown values default to
    /// "Basic" (the safest tier, fewest privileges) and log a warning so a
    /// future Dataverse-side schema change is diagnosable from backend logs
    /// instead of silently passing the verifier.
    /// </summary>
    private string MapDepthEnum(string? depth) => depth?.Trim() switch
    {
        "Basic" or "basic" => "Basic",
        "Local" or "local" => "Local",
        "Deep" or "deep" => "Deep",
        "Global" or "global" => "Global",
        _ => LogUnknownDepth(depth),
    };

    private string LogUnknownDepth(string? depth)
    {
        _logger.LogWarning(
            "Unknown PrivilegeDepth value '{Depth}' returned by Dataverse — defaulting to Basic. "
            + "If this recurs, the Dataverse role-privilege API contract may have changed.",
            depth ?? "<null>");
        return "Basic";
    }

    private static PrivilegeMetadata MapPrivilege(PrivilegeRow p)
    {
        // PrivilegeType is the AccessRight enum:
        //   Create=0, Read=1, Write=2, Delete=3, Assign=4, Share=5, Append=6, AppendTo=7
        // The Web API serializes as the string name when $expand'd — double-check
        // both forms because some envs return the int.
        var accessRight = p.PrivilegeType ?? string.Empty;
        if (int.TryParse(accessRight, out var intVal))
        {
            accessRight = intVal switch
            {
                0 => "Create",
                1 => "Read",
                2 => "Write",
                3 => "Delete",
                4 => "Assign",
                5 => "Share",
                6 => "Append",
                7 => "AppendTo",
                _ => accessRight,
            };
        }
        return new PrivilegeMetadata(
            PrivilegeId: p.PrivilegeId,
            Name: p.Name ?? string.Empty,
            AccessRight: accessRight,
            CanBeBasic: p.CanBeBasic ?? false,
            CanBeLocal: p.CanBeLocal ?? false,
            CanBeDeep: p.CanBeDeep ?? false,
            CanBeGlobal: p.CanBeGlobal ?? false);
    }

    private sealed class EntityWithPrivileges
    {
        [JsonPropertyName("LogicalName")] public string? LogicalName { get; set; }
        [JsonPropertyName("Privileges")] public List<PrivilegeRow>? Privileges { get; set; }
    }

    private sealed class PrivilegeRow
    {
        [JsonPropertyName("PrivilegeId")] public Guid PrivilegeId { get; set; }
        [JsonPropertyName("Name")] public string? Name { get; set; }
        [JsonPropertyName("PrivilegeType")] public string? PrivilegeType { get; set; }
        [JsonPropertyName("CanBeBasic")] public bool? CanBeBasic { get; set; }
        [JsonPropertyName("CanBeLocal")] public bool? CanBeLocal { get; set; }
        [JsonPropertyName("CanBeDeep")] public bool? CanBeDeep { get; set; }
        [JsonPropertyName("CanBeGlobal")] public bool? CanBeGlobal { get; set; }
    }

    private sealed class PrivilegeRowMin
    {
        [JsonPropertyName("privilegeid")] public Guid PrivilegeId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class BusinessUnitRow
    {
        [JsonPropertyName("businessunitid")] public Guid BusinessUnitId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("_parentbusinessunitid_value")] public string? ParentBusinessUnitId { get; set; }
    }

    private sealed class RoleTemplateRow
    {
        [JsonPropertyName("roletemplateid")] public Guid RoleTemplateId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    private sealed class RolePrivilegesActionResponse
    {
        [JsonPropertyName("RolePrivileges")] public List<RolePrivilegeRow>? RolePrivileges { get; set; }
    }

    private sealed class RolePrivilegeRow
    {
        [JsonPropertyName("PrivilegeId")] public Guid PrivilegeId { get; set; }
        [JsonPropertyName("Depth")] public string? Depth { get; set; }
    }
}
