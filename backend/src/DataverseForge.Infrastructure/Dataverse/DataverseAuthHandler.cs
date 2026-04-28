using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed class DataverseAuthHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<DataverseOptions> _options;
    private readonly ILogger<DataverseAuthHandler> _logger;
    private readonly object _credentialLock = new();
    private TokenCredential? _credential;
    private string? _credentialKey;
    private AccessToken _cachedToken;
    private string? _cachedTokenKey;

    public DataverseAuthHandler(
        IOptionsMonitor<DataverseOptions> options,
        ILogger<DataverseAuthHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;

        // If request URI is relative, fix it to use current EnvironmentUrl
        if (request.RequestUri != null && !request.RequestUri.IsAbsoluteUri)
        {
            if (!string.IsNullOrWhiteSpace(opts.EnvironmentUrl))
            {
                var baseUri = new Uri(opts.EnvironmentUrl.TrimEnd('/') + "/");
                request.RequestUri = new Uri(baseUri, request.RequestUri);
            }
        }

        var token = await GetTokenAsync(opts, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetTokenAsync(DataverseOptions opts, CancellationToken cancellationToken)
    {
        var key = $"{opts.TenantId}|{opts.ClientId}|{opts.EnvironmentUrl}";

        // Reuse cached token if still valid AND options haven't changed
        if (_cachedTokenKey == key && _cachedToken.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return _cachedToken.Token;
        }

        var credential = GetOrCreateCredential(opts, key);
        var scope = opts.EnvironmentUrl.TrimEnd('/') + "/.default";

        _logger.LogDebug("Acquiring Dataverse token for scope {Scope}", scope);

        _cachedToken = await credential.GetTokenAsync(
            new TokenRequestContext([scope]),
            cancellationToken);
        _cachedTokenKey = key;

        _logger.LogDebug("Token acquired, expires at {ExpiresOn}", _cachedToken.ExpiresOn);

        return _cachedToken.Token;
    }

    private TokenCredential GetOrCreateCredential(DataverseOptions opts, string key)
    {
        lock (_credentialLock)
        {
            // Rebuild credential when settings change
            if (_credential is not null && _credentialKey == key)
                return _credential;

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
            _credentialKey = key;

            return _credential;
        }
    }
}
