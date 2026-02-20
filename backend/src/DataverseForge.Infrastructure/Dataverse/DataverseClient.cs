using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed class DataverseClient : IDataverseClient
{
    private readonly HttpClient _http;
    private readonly IOptions<DataverseOptions> _options;
    private readonly ILogger<DataverseClient> _logger;

    public DataverseClient(
        HttpClient http,
        IOptions<DataverseOptions> options,
        ILogger<DataverseClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Calling WhoAmI endpoint");
        var whoAmI = await _http.GetFromJsonAsync<WhoAmIResponse>(
            "api/data/v9.2/WhoAmI", cancellationToken)
            ?? throw new InvalidOperationException("WhoAmI returned null");

        _logger.LogDebug("WhoAmI succeeded: OrgId={OrganizationId}", whoAmI.OrganizationId);

        var orgsResponse = await _http.GetFromJsonAsync<ODataResponse<OrganizationResponse>>(
            "api/data/v9.2/organizations?$select=name,languagecode,version,_basecurrencyid_value,isauditenabled",
            cancellationToken)
            ?? throw new InvalidOperationException("Organizations query returned null");

        var org = orgsResponse.Value.FirstOrDefault()
            ?? throw new InvalidOperationException("No organization found");

        return new EnvironmentInfo(
            OrganizationName: org.Name,
            EnvironmentUrl: _options.Value.EnvironmentUrl,
            BaseLanguageLcid: org.LanguageCode,
            DataverseVersion: org.Version,
            Region: string.Empty,
            BaseCurrencyCode: org.BaseCurrencyId ?? string.Empty,
            IsAuditEnabled: org.IsAuditEnabled?.Value ?? false);
    }

    public async Task<IReadOnlyList<SolutionInfo>> GetSolutionsListAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying unmanaged solutions");

        var response = await _http.GetFromJsonAsync<ODataResponse<SolutionResponse>>(
            "api/data/v9.2/solutions?$filter=ismanaged eq false&$select=solutionid,uniquename,friendlyname,version,ismanaged,modifiedon&$expand=publisherid($select=friendlyname)&$orderby=modifiedon desc",
            cancellationToken)
            ?? throw new InvalidOperationException("Solutions query returned null");

        _logger.LogDebug("Found {Count} unmanaged solutions", response.Value.Count);

        return response.Value
            .Select(s => new SolutionInfo(
                SolutionId: s.SolutionId,
                UniqueName: s.UniqueName,
                DisplayName: s.FriendlyName,
                Version: s.Version,
                PublisherName: s.PublisherId?.FriendlyName ?? string.Empty,
                IsManaged: s.IsManaged,
                ComponentCount: 0,
                ModifiedOn: s.ModifiedOn))
            .ToList()
            .AsReadOnly();
    }

    #region OData DTOs

    private sealed class WhoAmIResponse
    {
        [JsonPropertyName("OrganizationId")]
        public Guid OrganizationId { get; set; }

        [JsonPropertyName("BusinessUnitId")]
        public Guid BusinessUnitId { get; set; }

        [JsonPropertyName("UserId")]
        public Guid UserId { get; set; }
    }

    private sealed class ODataResponse<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = [];
    }

    private sealed class OrganizationResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("languagecode")]
        public int LanguageCode { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("_basecurrencyid_value")]
        public string? BaseCurrencyId { get; set; }

        [JsonPropertyName("isauditenabled")]
        public BooleanManagedProperty? IsAuditEnabled { get; set; }
    }

    private sealed class BooleanManagedProperty
    {
        [JsonPropertyName("Value")]
        public bool Value { get; set; }
    }

    private sealed class SolutionResponse
    {
        [JsonPropertyName("solutionid")]
        public Guid SolutionId { get; set; }

        [JsonPropertyName("uniquename")]
        public string UniqueName { get; set; } = string.Empty;

        [JsonPropertyName("friendlyname")]
        public string FriendlyName { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("ismanaged")]
        public bool IsManaged { get; set; }

        [JsonPropertyName("modifiedon")]
        public DateTime ModifiedOn { get; set; }

        [JsonPropertyName("publisherid")]
        public PublisherRef? PublisherId { get; set; }
    }

    private sealed class PublisherRef
    {
        [JsonPropertyName("friendlyname")]
        public string FriendlyName { get; set; } = string.Empty;
    }

    #endregion
}
