using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Settings;

public sealed class SettingsTestService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SettingsTestService> _logger;

    public SettingsTestService(IHttpClientFactory httpClientFactory, ILogger<SettingsTestService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<TestConnectionResult> TestAsync(string service, SettingsDto settings)
    {
        return service.ToLowerInvariant() switch
        {
            "azureai" => await TestAzureAiAsync(settings.AzureAiEndpoint, settings.AzureAiApiKey, settings.AzureAiDefaultModel),
            "dataverse" => await TestDataverseAsync(settings.DataverseEnvironmentUrl, settings.DataverseTenantId, settings.DataverseClientId, settings.DataverseClientSecret),
            "github" => await TestGitHubAsync(settings.GitHubToken),
            _ => new TestConnectionResult(false, $"Unknown service: {service}")
        };
    }

    private async Task<TestConnectionResult> TestAzureAiAsync(string endpoint, string apiKey, string model)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return new TestConnectionResult(false, "Endpoint is required");

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("••••"))
            return new TestConnectionResult(false, "Please enter a valid API key (not masked)");

        try
        {
            // Strip any path/query the user pasted (e.g., full chat completions URL)
            var trimmed = NormalizeEndpoint(endpoint);
            var isAzureOpenAi = trimmed.Contains("cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase)
                             || trimmed.Contains("openai.azure.com", StringComparison.OrdinalIgnoreCase);

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("api-key", apiKey);

            // Probe the actual chat completions endpoint with a minimal request — that's the
            // path the agent will use, so it's the one that matters. Listing endpoints
            // (/openai/deployments, /models) aren't always exposed on Cognitive Services
            // Foundry-style resources, so they 404 even when chat works.
            HttpResponseMessage probe;
            string probeLabel;

            if (isAzureOpenAi)
            {
                if (string.IsNullOrWhiteSpace(model))
                    return new TestConnectionResult(false, "Default model / deployment name is required for Azure OpenAI endpoints");

                var url = $"{trimmed}/openai/deployments/{Uri.EscapeDataString(model)}/chat/completions?api-version=2024-08-01-preview";
                probe = await PostMinimalChatAsync(client, url, includeModelInBody: false, model);
                probeLabel = "Azure OpenAI";
            }
            else
            {
                // Azure AI Foundry / Inference unified API
                var url = $"{trimmed}/chat/completions?api-version=2024-05-01-preview";
                probe = await PostMinimalChatAsync(client, url, includeModelInBody: true, model);
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
                    var body = await probe.Content.ReadAsStringAsync();
                    if (!string.IsNullOrWhiteSpace(body) && body.Length < 400)
                        detail = $" — {body.Trim()}";
                }
                catch { /* ignore */ }
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
        HttpClient client, string url, bool includeModelInBody, string model)
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
        return await client.PostAsync(url, content);
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

    private async Task<TestConnectionResult> TestDataverseAsync(string envUrl, string tenantId, string clientId, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(envUrl))
            return new TestConnectionResult(false, "Environment URL is required");

        try
        {
            // Use MSAL to acquire a token and call WhoAmI
            var app = Microsoft.Identity.Client.ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .Build();

            var result = await app.AcquireTokenForClient(
                new[] { $"{envUrl.TrimEnd('/')}/.default" })
                .ExecuteAsync();

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.AccessToken);

            var trimmedUrl = envUrl.TrimEnd('/');
            var whoAmI = await client.GetAsync($"{trimmedUrl}/api/data/v9.2/WhoAmI()");
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
                    $"{trimmedUrl}/api/data/v9.2/RetrieveCurrentOrganization(AccessType=Microsoft.Dynamics.CRM.EndpointAccessType'Default')");
                if (orgRes.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(
                        await orgRes.Content.ReadAsStringAsync());
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dataverse connection test failed");
            return new TestConnectionResult(false, $"Connection failed: {ex.Message}");
        }
    }

    private async Task<TestConnectionResult> TestGitHubAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new TestConnectionResult(false, "Token is required");

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            client.DefaultRequestHeaders.Add("User-Agent", "DataverseAIForge");

            var response = await client.GetAsync("https://api.github.com/user");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return new TestConnectionResult(true, "Authenticated with GitHub successfully");
            }

            return new TestConnectionResult(false, $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitHub connection test failed");
            return new TestConnectionResult(false, $"Connection failed: {ex.Message}");
        }
    }
}
