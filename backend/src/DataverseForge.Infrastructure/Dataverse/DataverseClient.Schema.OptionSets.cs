using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

// Global + local option-set operations (the GlobalOptionSetDefinitions
// collection plus per-field OptionMetadata flows). Carved out of
// DataverseClient.Schema.cs (946 LOC) on 2026-05-25 — Schema.cs keeps
// tables / fields / relationships / alternate keys / generic metadata
// read; this file owns the option-set surface.
//
// All members are `internal sealed partial class DataverseClient` so the
// shared `_http`, `ApiBase`, `_logger`, and `EnsureSuccessOrSurfaceAsync`
// helpers from the sibling partials are in scope.
internal sealed partial class DataverseClient
{
    public async Task<Guid> CreateGlobalOptionSetAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        // Log the FULL outgoing payload at Information level. Past pattern: option-set
        // 400s with no actionable detail because the agent's textual report ("unreadable
        // payload") loses the specifics. Info-level request logging + EnsureSuccessOrSurfaceAsync
        // response surfacing keeps the backend log diagnosable; correlate by timestamp
        // when reading both sides of the wire.
        _logger.LogInformation("Creating global option set via GlobalOptionSetDefinitions. Payload: {Payload}", webApiPayloadJson);

        return await PostForMetadataIdAsync(
            $"{ApiBase}GlobalOptionSetDefinitions", webApiPayloadJson,
            "Create global option set (GlobalOptionSetDefinitions)", cancellationToken);
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

        await PostVoidAsync($"{ApiBase}InsertOptionValue", webApiPayloadJson, "InsertOptionValue", cancellationToken);
    }

    public async Task InsertStatusValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Inserting status value via InsertStatusValue");

        await PostVoidAsync($"{ApiBase}InsertStatusValue", webApiPayloadJson, "InsertStatusValue", cancellationToken);
    }

    public async Task UpdateOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating option value via UpdateOptionValue");

        await PostVoidAsync($"{ApiBase}UpdateOptionValue", webApiPayloadJson, "UpdateOptionValue", cancellationToken);
    }

    public async Task DeleteOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting option value via DeleteOptionValue");

        await PostVoidAsync($"{ApiBase}DeleteOptionValue", webApiPayloadJson, "DeleteOptionValue", cancellationToken);
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
}
