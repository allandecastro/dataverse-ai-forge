using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed partial class DataverseClient
{
    public async Task<IReadOnlyList<SolutionInfo>> GetSolutionsListAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying unmanaged solutions");

        var response = await _http.GetFromJsonAsync<ODataResponse<SolutionResponse>>(
            $"{ApiBase}solutions?$filter=ismanaged eq false&$select=solutionid,uniquename,friendlyname,version,ismanaged,modifiedon&$expand=publisherid($select=publisherid,friendlyname,uniquename,customizationprefix)&$orderby=modifiedon desc",
            cancellationToken)
            ?? throw new InvalidOperationException("Solutions query returned null");

        _logger.LogDebug("Found {Count} unmanaged solutions", response.Value.Count);

        // Fetch component counts in parallel — one count query per solution
        var countTasks = response.Value.Select(s => GetSolutionComponentCountAsync(s.SolutionId, cancellationToken));
        var counts = await Task.WhenAll(countTasks);

        return response.Value
            .Select((s, i) => new SolutionInfo(
                SolutionId: s.SolutionId,
                UniqueName: s.UniqueName,
                DisplayName: s.FriendlyName,
                Version: s.Version,
                PublisherName: s.PublisherId?.FriendlyName ?? string.Empty,
                PublisherId: s.PublisherId?.PublisherId ?? Guid.Empty,
                PublisherUniqueName: s.PublisherId?.UniqueName ?? string.Empty,
                PublisherPrefix: s.PublisherId?.CustomizationPrefix ?? string.Empty,
                IsManaged: s.IsManaged,
                ComponentCount: counts[i],
                ModifiedOn: s.ModifiedOn))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<PublisherInfo>> GetPublishersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying non-readonly publishers");

        var response = await _http.GetFromJsonAsync<ODataResponse<PublisherResponse>>(
            $"{ApiBase}publishers?$filter=isreadonly eq false&$select=publisherid,friendlyname,uniquename,customizationprefix,customizationoptionvalueprefix",
            cancellationToken)
            ?? throw new InvalidOperationException("Publishers query returned null");

        _logger.LogDebug("Found {Count} publishers", response.Value.Count);

        return response.Value
            .Select(p => new PublisherInfo(
                PublisherId: p.PublisherId,
                DisplayName: p.FriendlyName,
                UniqueName: p.UniqueName,
                Prefix: p.CustomizationPrefix,
                OptionValuePrefix: p.CustomizationOptionValuePrefix))
            .ToList()
            .AsReadOnly();
    }

    public async Task<Guid> CreatePublisherAsync(
        string uniqueName,
        string friendlyName,
        string description,
        string customizationPrefix,
        int customizationOptionValuePrefix,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating publisher '{UniqueName}' (prefix: {Prefix}, optionPrefix: {OptionPrefix})",
            uniqueName, customizationPrefix, customizationOptionValuePrefix);

        var payload = new Dictionary<string, object>
        {
            ["uniquename"] = uniqueName,
            ["friendlyname"] = friendlyName,
            ["description"] = description,
            ["customizationprefix"] = customizationPrefix.ToLowerInvariant(),
            ["customizationoptionvalueprefix"] = customizationOptionValuePrefix
        };

        var json = JsonSerializer.Serialize(payload);
        _logger.LogInformation("CreatePublisher payload: {Payload}", json);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}publishers", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("CreatePublisher failed: {Status} - sent={Payload} - response={Body}",
                (int)response.StatusCode, json, body);
            throw new HttpRequestException(
                $"Dataverse returned {(int)response.StatusCode}: {body}");
        }

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task<Guid> CreateSolutionAsync(
        string uniqueName,
        string friendlyName,
        string description,
        string version,
        Guid publisherId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating unmanaged solution '{UniqueName}' with publisher {PublisherId}", uniqueName, publisherId);

        // The single-valued navigation property on solution pointing to publisher is named
        // `publisherid` (per the publisher entity's collection-nav partner column).
        var payload = new Dictionary<string, object>
        {
            ["uniquename"] = uniqueName,
            ["friendlyname"] = friendlyName,
            ["description"] = description,
            ["version"] = string.IsNullOrWhiteSpace(version) ? "1.0.0.0" : version,
            ["publisherid@odata.bind"] = $"/publishers({publisherId})"
        };

        var json = JsonSerializer.Serialize(payload);
        _logger.LogInformation("CreateSolution payload: {Payload}", json);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}solutions", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("CreateSolution failed: {Status} - sent={Payload} - response={Body}",
                (int)response.StatusCode, json, body);
            throw new HttpRequestException(
                $"Dataverse returned {(int)response.StatusCode}: {body}");
        }

        return ExtractMetadataIdFromHeader(response);
    }

    public async Task UpdateSolutionVersionAsync(Guid solutionId, string newVersion, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating solution {SolutionId} version to {NewVersion}", solutionId, newVersion);

        var payload = new Dictionary<string, object> { ["version"] = newVersion };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{ApiBase}solutions({solutionId})")
        {
            Content = content
        };
        var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("UpdateSolutionVersion failed: {Status} - sent={Payload} - response={Body}",
                (int)response.StatusCode, json, body);
            throw new HttpRequestException($"Dataverse returned {(int)response.StatusCode}: {body}");
        }
    }

    public async Task PublishAllAsync(CancellationToken cancellationToken = default)
    {
        // PublishAllXml is an unbound action with no parameters — POST an
        // empty JSON body. Returns 204 on success. Can take a while on large
        // environments; the caller's cancellation token bounds it.
        _logger.LogInformation("Publishing all customizations (PublishAllXml)");

        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiBase}PublishAllXml", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "PublishAllXml", cancellationToken);
    }

    public async Task<OrganizationResources> RetrieveOrganizationResourcesAsync(CancellationToken cancellationToken = default)
    {
        // RetrieveOrganizationResources is a FUNCTION (not action) — invoked
        // via GET, not POST. Returns RetrieveOrganizationResourcesResponse with
        // a single OrganizationResources object.
        _logger.LogDebug("Retrieving organization resources");

        var response = await _http.GetAsync($"{ApiBase}RetrieveOrganizationResources()", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "RetrieveOrganizationResources", cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("RetrieveOrganizationResources returned an empty body.");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("OrganizationResources", out var res))
        {
            throw new InvalidOperationException(
                $"RetrieveOrganizationResources response did not include OrganizationResources. Body: {body}");
        }

        int Read(string name) => res.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : 0;

        return new OrganizationResources(
            CurrentNumberOfActiveUsers: Read("CurrentNumberOfActiveUsers"),
            MaxNumberOfActiveUsers: Read("MaxNumberOfActiveUsers"),
            CurrentNumberOfNonInteractiveUsers: Read("CurrentNumberOfNonInteractiveUsers"),
            MaxNumberOfNonInteractiveUsers: Read("MaxNumberOfNonInteractiveUsers"),
            CurrentNumberOfCustomEntities: Read("CurrentNumberOfCustomEntities"),
            MaxNumberOfCustomEntities: Read("MaxNumberOfCustomEntities"),
            CurrentNumberOfPublishedWorkflows: Read("CurrentNumberOfPublishedWorkflows"),
            MaxNumberOfPublishedWorkflows: Read("MaxNumberOfPublishedWorkflows"),
            CurrentStorage: Read("CurrentStorage"),
            MaxStorage: Read("MaxStorage"));
    }

    public async Task<byte[]> ExportSolutionAsync(string solutionUniqueName, bool managed, CancellationToken cancellationToken = default)
    {
        // ExportSolution is an unbound action — POST to /ExportSolution with
        // SolutionName + Managed (and the optional booleans, defaulted false).
        // Returns ExportSolutionResponse { ExportSolutionFile: Edm.Binary }, which
        // OData represents as a base64-encoded string in the JSON body. We
        // decode and return raw bytes; the caller persists them to disk.
        _logger.LogInformation("Exporting solution {Solution} (managed={Managed})", solutionUniqueName, managed);

        var requestBody = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["SolutionName"] = solutionUniqueName,
            ["Managed"] = managed,
            // The remaining settings booleans default to false on the server side
            // when omitted — we don't need to send them. Pass them explicitly only
            // if the user asks for a specific subset.
        });
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        // ExportSolution can take a while for large solutions. Don't trip over
        // the default HttpClient timeout — the call is bounded by Dataverse's
        // own action timeout (≈10 min) and the cancellationToken.
        var response = await _http.PostAsync($"{ApiBase}ExportSolution", content, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, $"ExportSolution('{solutionUniqueName}')", cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException($"ExportSolution('{solutionUniqueName}') returned an empty body.");
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("ExportSolutionFile", out var fileEl)
            || fileEl.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"ExportSolution('{solutionUniqueName}') response did not include ExportSolutionFile. Body: {Truncate(body, 500)}");
        }
        var base64 = fileEl.GetString() ?? string.Empty;
        return Convert.FromBase64String(base64);
    }

    public async Task AddSolutionComponentAsync(Guid componentId, SolutionComponentType componentType, string solutionUniqueName, bool doNotIncludeSubcomponents = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Adding component {ComponentId} (type {ComponentType}, subcomponents={IncludeSubs}) to solution {Solution}",
            componentId, componentType, !doNotIncludeSubcomponents, solutionUniqueName);

        var request = new AddSolutionComponentRequest
        {
            ComponentId = componentId,
            ComponentType = (int)componentType,
            SolutionUniqueName = solutionUniqueName,
            AddRequiredComponents = false,
            DoNotIncludeSubcomponents = doNotIncludeSubcomponents,
            IncludedComponentSettingsValues = null
        };

        var response = await _http.PostAsJsonAsync($"{ApiBase}AddSolutionComponent", request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Add solution component", cancellationToken);
    }

    public async Task RemoveSolutionComponentAsync(Guid componentId, SolutionComponentType componentType, string solutionUniqueName, CancellationToken cancellationToken = default)
    {
        // Mirror of AddSolutionComponent — same body shape, RemoveSolutionComponent action.
        _logger.LogInformation("Removing component {ComponentId} (type {ComponentType}) from solution {Solution}",
            componentId, componentType, solutionUniqueName);

        var request = new AddSolutionComponentRequest
        {
            ComponentId = componentId,
            ComponentType = (int)componentType,
            SolutionUniqueName = solutionUniqueName,
            AddRequiredComponents = false,
            IncludedComponentSettingsValues = null
        };

        var response = await _http.PostAsJsonAsync($"{ApiBase}RemoveSolutionComponent", request, cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Remove solution component", cancellationToken);
    }

    public async Task DeleteSolutionAsync(Guid solutionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting solution {SolutionId}", solutionId);
        var response = await _http.DeleteAsync($"{ApiBase}solutions({solutionId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Delete solution", cancellationToken);
    }

    public async Task DeletePublisherAsync(Guid publisherId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting publisher {PublisherId}", publisherId);
        var response = await _http.DeleteAsync($"{ApiBase}publishers({publisherId})", cancellationToken);
        await EnsureSuccessOrSurfaceAsync(response, "Delete publisher", cancellationToken);
    }
}
