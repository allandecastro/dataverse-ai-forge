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
    private readonly DataverseCredentialCache _credentialCache;

    public DataverseAuthHandler(
        IOptionsMonitor<DataverseOptions> options,
        DataverseCredentialCache credentialCache,
        ILogger<DataverseAuthHandler> logger)
    {
        _options = options;
        _credentialCache = credentialCache;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;

        if (string.IsNullOrWhiteSpace(opts.EnvironmentUrl))
        {
            throw new InvalidOperationException(
                "Dataverse is not configured. Please set EnvironmentUrl, TenantId, ClientId, ClientSecret in Settings.");
        }

        // Rewrite the URL using the current EnvironmentUrl, replacing the placeholder host
        if (request.RequestUri != null)
        {
            var baseUri = new Uri(opts.EnvironmentUrl.TrimEnd('/') + "/");
            var pathAndQuery = request.RequestUri.IsAbsoluteUri
                ? request.RequestUri.PathAndQuery.TrimStart('/')
                : request.RequestUri.OriginalString;
            request.RequestUri = new Uri(baseUri, pathAndQuery);
        }

        var token = await GetTokenAsync(opts, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        // F-39: surface ONLY the URL + status at Warning when a request fails.
        // Earlier this handler also read + logged the entire response body and
        // re-buffered the content so callers could read it again. That had two
        // problems: (1) it leaked raw response bodies (potentially containing
        // schema names / customer values from a failed POST payload's echoed
        // form) into Warning logs that ops aggregators forward, (2) every
        // error was double-buffered because the downstream
        // EnsureSuccessOrSurfaceAsync also reads the body. The structured
        // OData `error.message` is now surfaced via the exception thrown by
        // EnsureSuccessOrSurfaceAsync — that's the correct layer. If verbose
        // logging is needed for diagnosis, the DataverseLoggingHandler still
        // covers both directions when its VerboseLogging flag is on.
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Dataverse {Method} {Url} → {Status} {Reason}",
                request.Method, request.RequestUri, (int)response.StatusCode, response.ReasonPhrase);
        }

        return response;
    }

    private async Task<string> GetTokenAsync(DataverseOptions opts, CancellationToken cancellationToken)
    {
        // Cache key includes AuthMode so flipping SP↔Interactive forces a credential
        // rebuild (otherwise the cached SP credential would keep being used).
        var key = $"{opts.AuthMode}|{opts.TenantId}|{opts.ClientId}|{opts.EnvironmentUrl}";

        // Reuse cached token if still valid AND options haven't changed.
        var cached = _credentialCache.GetCachedToken(key);
        if (cached.HasValue) return cached.Value.Token;

        var credential = _credentialCache.GetCachedCredential(key);
        if (credential is null)
        {
            credential = BuildCredential(opts);
            _credentialCache.SetCredential(key, credential);
        }

        // Scope per Microsoft Learn guidance: public clients (Interactive) use
        // <env>/user_impersonation; confidential clients (ServicePrincipal) use
        // <env>/.default. Mixing them up surfaces as cryptic AADSTS errors at
        // token-acquisition time.
        var envBase = opts.EnvironmentUrl.TrimEnd('/');
        var scope = opts.AuthMode == DataverseAuthMode.Interactive
            ? envBase + "/user_impersonation"
            : envBase + "/.default";

        _logger.LogDebug("Acquiring Dataverse token for scope {Scope}", scope);

        var token = await credential.GetTokenAsync(
            new TokenRequestContext([scope]),
            cancellationToken);
        _credentialCache.SetToken(key, token);

        _logger.LogDebug("Token acquired, expires at {ExpiresOn}", token.ExpiresOn);

        return token.Token;
    }

    private TokenCredential BuildCredential(DataverseOptions opts)
    {
        if (opts.AuthMode == DataverseAuthMode.Interactive)
        {
            // No App Registration? Use Microsoft's published public client. It's
            // multi-tenant with http://localhost pre-configured as a redirect URI,
            // so the user only has to (a) have a Dataverse user account, (b) click
            // through the browser sign-in. No setup at all.
            //
            // If the user DID provide their own ClientId, honor it — they may want
            // a custom App Registration with specific permissions or audit trail.
            var clientId = string.IsNullOrWhiteSpace(opts.ClientId)
                ? DataverseConstants.WellKnownPublicClientId
                : opts.ClientId;
            // Same idea for tenant: empty → "organizations" (any work/school
            // account, the user picks during sign-in). Custom value → honored.
            var tenantId = string.IsNullOrWhiteSpace(opts.TenantId)
                ? DataverseConstants.OrganizationsAuthority
                : opts.TenantId;

            _logger.LogInformation(
                "Using InteractiveBrowserCredential (Tenant={TenantId}, ClientId={ClientId}, UsingPublicClient={UsingPublicClient}). "
                + "A browser tab will open for sign-in on first token acquisition.",
                tenantId, clientId, clientId == DataverseConstants.WellKnownPublicClientId);

            var browserOpts = new InteractiveBrowserCredentialOptions
            {
                TenantId = tenantId,
                ClientId = clientId,
                // Default redirect URI is http://localhost — already configured on
                // Microsoft's public client and on any App Registration set up
                // per the README "Interactive" instructions.
            };
            return new InteractiveBrowserCredential(browserOpts);
        }

        // ServicePrincipal path — requires all three of Tenant/Client/Secret.
        if (!string.IsNullOrWhiteSpace(opts.TenantId) &&
            !string.IsNullOrWhiteSpace(opts.ClientId) &&
            !string.IsNullOrWhiteSpace(opts.ClientSecret))
        {
            _logger.LogInformation("Using ClientSecretCredential for Dataverse authentication");
            return new ClientSecretCredential(opts.TenantId, opts.ClientId, opts.ClientSecret);
        }

        // Last-resort fallback — picks up Azure CLI / managed identity / env vars.
        _logger.LogInformation("Using DefaultAzureCredential for Dataverse authentication");
        return new DefaultAzureCredential();
    }
}
