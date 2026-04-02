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
            "azureai" => await TestAzureAiAsync(settings.AzureAiEndpoint, settings.AzureAiApiKey),
            "dataverse" => await TestDataverseAsync(settings.DataverseEnvironmentUrl, settings.DataverseTenantId, settings.DataverseClientId, settings.DataverseClientSecret),
            "github" => await TestGitHubAsync(settings.GitHubToken),
            _ => new TestConnectionResult(false, $"Unknown service: {service}")
        };
    }

    private async Task<TestConnectionResult> TestAzureAiAsync(string endpoint, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return new TestConnectionResult(false, "Endpoint is required");

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("api-key", apiKey);

            var url = $"{endpoint.TrimEnd('/')}/models?api-version=2024-05-01-preview";
            var response = await client.GetAsync(url);

            return response.IsSuccessStatusCode
                ? new TestConnectionResult(true, "Connected to Azure AI Foundry successfully")
                : new TestConnectionResult(false, $"Azure AI returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure AI connection test failed");
            return new TestConnectionResult(false, $"Connection failed: {ex.Message}");
        }
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

            var response = await client.GetAsync($"{envUrl.TrimEnd('/')}/api/data/v9.2/WhoAmI()");

            return response.IsSuccessStatusCode
                ? new TestConnectionResult(true, "Connected to Dataverse successfully")
                : new TestConnectionResult(false, $"Dataverse returned {(int)response.StatusCode} {response.ReasonPhrase}");
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
