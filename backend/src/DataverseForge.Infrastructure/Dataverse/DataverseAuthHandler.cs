using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed class DataverseAuthHandler : DelegatingHandler
{
    private readonly IOptions<DataverseOptions> _options;
    private readonly ILogger<DataverseAuthHandler> _logger;
    private TokenCredential? _credential;
    private AccessToken _cachedToken;

    public DataverseAuthHandler(
        IOptions<DataverseOptions> options,
        ILogger<DataverseAuthHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return _cachedToken.Token;
        }

        var credential = GetOrCreateCredential();
        var scope = _options.Value.EnvironmentUrl.TrimEnd('/') + "/.default";

        _logger.LogDebug("Acquiring Dataverse token for scope {Scope}", scope);

        _cachedToken = await credential.GetTokenAsync(
            new TokenRequestContext([scope]),
            cancellationToken);

        _logger.LogDebug("Token acquired, expires at {ExpiresOn}", _cachedToken.ExpiresOn);

        return _cachedToken.Token;
    }

    private TokenCredential GetOrCreateCredential()
    {
        if (_credential is not null)
            return _credential;

        var opts = _options.Value;

        if (!string.IsNullOrWhiteSpace(opts.TenantId) &&
            !string.IsNullOrWhiteSpace(opts.ClientId) &&
            !string.IsNullOrWhiteSpace(opts.ClientSecret))
        {
            _logger.LogInformation("Using ClientSecretCredential for Dataverse authentication");
            _credential = new ClientSecretCredential(opts.TenantId, opts.ClientId, opts.ClientSecret);
        }
        else
        {
            _logger.LogInformation("Using DefaultAzureCredential for Dataverse authentication");
            _credential = new DefaultAzureCredential();
        }

        return _credential;
    }
}
