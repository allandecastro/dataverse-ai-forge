using DataverseForge.Infrastructure.Dataverse;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Settings;

public sealed class SettingsTestService
{
    // Probe timeout for Test Connection HTTP calls. The Settings UI shows a spinner
    // while we wait, so 10s is the upper bound users tolerate before assuming the
    // app is hung.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SettingsTestService> _logger;

    public SettingsTestService(IHttpClientFactory httpClientFactory, ILogger<SettingsTestService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<TestConnectionResult> TestAsync(string service, SettingsDto settings, CancellationToken cancellationToken = default)
    {
        return service.ToLowerInvariant() switch
        {
            "azureai" => await TestAzureAiAsync(settings.AzureAiEndpoint, settings.AzureAiApiKey, settings.AzureAiDefaultModel, cancellationToken),
            "dataverse" => await TestDataverseAsync(settings.DataverseEnvironmentUrl, settings.DataverseTenantId, settings.DataverseClientId, settings.DataverseClientSecret, settings.DataverseAuthMode, cancellationToken),
            _ => new TestConnectionResult(false, $"Unknown service: {service}")
        };
    }

    private async Task<TestConnectionResult> TestAzureAiAsync(string endpoint, string apiKey, string model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return new TestConnectionResult(false, "Endpoint is required");

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("••••"))
            return new TestConnectionResult(false, "Please enter a valid API key (not masked)");

        // Strip any path/query the user pasted (e.g., full chat completions URL)
        var trimmed = NormalizeEndpoint(endpoint);
        var isAzureOpenAi = trimmed.Contains("cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase)
                         || trimmed.Contains("openai.azure.com", StringComparison.OrdinalIgnoreCase);

        // Fail fast on missing deployment name BEFORE building the HTTP client — Azure
        // OpenAI URLs require it in the path, and a clear error here beats a 404 from
        // a half-built probe URL.
        if (isAzureOpenAi && string.IsNullOrWhiteSpace(model))
            return new TestConnectionResult(false, "Default model / deployment name is required for Azure OpenAI endpoints");

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = ProbeTimeout;
            client.DefaultRequestHeaders.Add("api-key", apiKey);

            // Probe the actual chat completions endpoint with a minimal request — that's the
            // path the agent will use, so it's the one that matters. Listing endpoints
            // (/openai/deployments, /models) aren't always exposed on Cognitive Services
            // Foundry-style resources, so they 404 even when chat works.
            HttpResponseMessage probe;
            string probeLabel;

            if (isAzureOpenAi)
            {
                var url = $"{trimmed}/openai/deployments/{Uri.EscapeDataString(model)}/chat/completions?api-version=2024-08-01-preview";
                probe = await PostMinimalChatAsync(client, url, includeModelInBody: false, model, cancellationToken);
                probeLabel = "Azure OpenAI";
            }
            else
            {
                // Azure AI Foundry / Inference unified API
                var url = $"{trimmed}/chat/completions?api-version=2024-05-01-preview";
                probe = await PostMinimalChatAsync(client, url, includeModelInBody: true, model, cancellationToken);
                probeLabel = "Azure AI Foundry";
            }

            if (probe.IsSuccessStatusCode)
            {
                return new TestConnectionResult(true,
                    string.IsNullOrWhiteSpace(model)
                        ? $"Connected to {probeLabel} successfully"
                        : $"Connected to {probeLabel} — model '{model}' reachable");
            }

            var status = (int)probe.StatusCode;
            var hint = status switch
            {
                401 or 403 => " — check the API key",
                404 => string.IsNullOrWhiteSpace(model)
                    ? " — endpoint URL does not match an Azure OpenAI or Foundry resource"
                    : $" — deployment '{model}' not found at this endpoint",
                _ => string.Empty
            };

            // Surface the response body for non-auth failures so users can see the real reason
            string detail = string.Empty;
            if (status != 401 && status != 403)
            {
                try
                {
                    var body = await probe.Content.ReadAsStringAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(body) && body.Length < 400)
                        detail = $" — {body.Trim()}";
                }
                catch (Exception bodyEx)
                {
                    // Body unreadable (network blip, malformed encoding, etc.) — non-fatal,
                    // we still return the status hint above. Logged at debug to keep the
                    // happy-path quiet but make diagnosis possible if a user reports an
                    // empty error message.
                    _logger.LogDebug(bodyEx, "Could not read response body for Azure AI {Status} probe", status);
                }
            }

            return new TestConnectionResult(false, $"Azure AI returned {status} {probe.ReasonPhrase}{hint}{detail}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure AI connection test failed");
            return new TestConnectionResult(false, $"Connection failed: {ex.Message}");
        }
    }

    private static async Task<HttpResponseMessage> PostMinimalChatAsync(
        HttpClient client, string url, bool includeModelInBody, string model, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["messages"] = new[] { new { role = "user", content = "ping" } },
            ["max_tokens"] = 1
        };
        if (includeModelInBody && !string.IsNullOrWhiteSpace(model))
            payload["model"] = model;

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await client.PostAsync(url, content, cancellationToken);
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Host}".TrimEnd('/');
        }
        return trimmed;
    }

    private async Task<TestConnectionResult> TestDataverseAsync(string envUrl, string tenantId, string clientId, string clientSecret, string authMode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(envUrl))
            return new TestConnectionResult(false, "Environment URL is required");

        var isInteractive = string.Equals(authMode, "Interactive", StringComparison.OrdinalIgnoreCase);

        // Mode-specific required fields.
        // - Interactive: Tenant + Client are BOTH optional (defaults to Microsoft's
        //   public client + "organizations" authority). Only env URL is required.
        // - ServicePrincipal: all three of Tenant + Client + Secret are required.
        if (!isInteractive)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return new TestConnectionResult(false, "Tenant ID is required");
            if (string.IsNullOrWhiteSpace(clientId)) return new TestConnectionResult(false, "Client ID is required");
            if (string.IsNullOrWhiteSpace(clientSecret) || clientSecret.Contains("••••"))
                return new TestConnectionResult(false, "Client Secret is required (and must not be masked)");
        }

        try
        {
            string accessToken;
            if (isInteractive)
            {
                // Default to Microsoft's published public client + organizations
                // authority when the user hasn't filled in their own values.
                var resolvedClientId = string.IsNullOrWhiteSpace(clientId)
                    ? DataverseConstants.WellKnownPublicClientId
                    : clientId;
                var resolvedTenantId = string.IsNullOrWhiteSpace(tenantId)
                    ? DataverseConstants.OrganizationsAuthority
                    : tenantId;

                var browserOpts = new Azure.Identity.InteractiveBrowserCredentialOptions
                {
                    TenantId = resolvedTenantId,
                    ClientId = resolvedClientId,
                };
                var credential = new Azure.Identity.InteractiveBrowserCredential(browserOpts);
                // F-45: link the inbound cancellation token with the 2-minute
                // sign-in window so the user can cancel the probe early (e.g.
                // by closing the browser tab or aborting the HTTP request).
                using var tokenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                tokenCts.CancelAfter(TimeSpan.FromMinutes(2));
                // Public clients use user_impersonation, not .default — see Microsoft
                // Learn "Use OAuth authentication with Microsoft Dataverse".
                var token = await credential.GetTokenAsync(
                    new Azure.Core.TokenRequestContext(new[] { $"{envUrl.TrimEnd('/')}/user_impersonation" }),
                    tokenCts.Token);
                accessToken = token.Token;
            }
            else
            {
                // SP path — MSAL ConfidentialClient with the secret.
                var app = Microsoft.Identity.Client.ConfidentialClientApplicationBuilder
                    .Create(clientId)
                    .WithClientSecret(clientSecret)
                    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                    .Build();

                var result = await app.AcquireTokenForClient(
                    new[] { $"{envUrl.TrimEnd('/')}/.default" })
                    .ExecuteAsync(cancellationToken);
                accessToken = result.AccessToken;
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = ProbeTimeout;
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var trimmedUrl = envUrl.TrimEnd('/');
            var whoAmI = await client.GetAsync($"{trimmedUrl}/{DataverseConstants.ApiBase}WhoAmI()", cancellationToken);
            if (!whoAmI.IsSuccessStatusCode)
            {
                return new TestConnectionResult(false,
                    $"Dataverse returned {(int)whoAmI.StatusCode} {whoAmI.ReasonPhrase}");
            }

            // Pull the org friendly name so the sidebar can show it immediately
            string? orgName = null;
            try
            {
                var orgRes = await client.GetAsync(
                    $"{trimmedUrl}/{DataverseConstants.ApiBase}RetrieveCurrentOrganization(AccessType=Microsoft.Dynamics.CRM.EndpointAccessType'Default')",
                    cancellationToken);
                if (orgRes.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(
                        await orgRes.Content.ReadAsStringAsync(cancellationToken));
                    if (doc.RootElement.TryGetProperty("Detail", out var detail) &&
                        detail.TryGetProperty("FriendlyName", out var friendly))
                    {
                        orgName = friendly.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not retrieve organization friendly name (probe still passes)");
            }

            return new TestConnectionResult(
                Success: true,
                Message: orgName is null
                    ? "Connected to Dataverse successfully"
                    : $"Connected to Dataverse — {orgName}",
                Environment: orgName,
                EnvironmentUrl: trimmedUrl);
        }
        catch (OperationCanceledException)
        {
            // The InteractiveBrowserCredential CancellationToken fires after the 2-minute
            // sign-in window. Most often this means the user closed the browser tab or
            // didn't get to it. A clear message beats the cryptic "OperationCanceledException".
            _logger.LogInformation("Dataverse Interactive sign-in cancelled or timed out");
            return new TestConnectionResult(false,
                "Sign-in was cancelled or timed out. Click Test again to reopen the browser.");
        }
        catch (Exception ex) when (LooksLikeUserCancellation(ex))
        {
            _logger.LogInformation(ex, "Dataverse Interactive sign-in cancelled by the user");
            return new TestConnectionResult(false, "Sign-in was cancelled. Click Test again to reopen the browser.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dataverse connection test failed");
            return new TestConnectionResult(false, $"Connection failed: {ex.Message}");
        }
    }

    private static bool LooksLikeUserCancellation(Exception ex)
    {
        // Azure.Identity raises AuthenticationFailedException wrapping MSAL exceptions.
        // The user-cancellation cases all carry one of these markers in the message.
        // Matching by string is brittle but the exception types live in different
        // assemblies (Azure.Identity / MSAL) so a typed catch would couple us tighter.
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("user_cancelled", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("authentication_canceled", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("AADSTS65004", StringComparison.OrdinalIgnoreCase) // user declined consent
            || msg.Contains("Authentication canceled", StringComparison.OrdinalIgnoreCase);
    }
}
