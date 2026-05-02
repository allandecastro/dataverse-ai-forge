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
