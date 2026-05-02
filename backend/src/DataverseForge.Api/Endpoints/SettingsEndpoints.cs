using DataverseForge.Infrastructure.AzureAI;
using DataverseForge.Infrastructure.Dataverse;
using DataverseForge.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Api.Endpoints;

/// <summary>
/// /api/settings — get masked, save (idempotent on masked values), and Test Connection.
/// Save also hot-reloads <see cref="ChatClientFactory"/> so the next chat turn picks
/// up the new credentials without restart.
/// </summary>
internal static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", (UserSettingsService settingsService) =>
        {
            var settings = settingsService.GetCurrentSettings();
            return Results.Ok(settings);
        });

        app.MapPut("/api/settings", async (
            SettingsDto dto,
            UserSettingsService settingsService,
            ChatClientFactory chatClientFactory,
            IConfiguration configuration) =>
        {
            await settingsService.SaveSettingsAsync(dto);

            // Force-reload configuration so IOptionsMonitor.CurrentValue picks up
            // new values immediately (no debounce wait).
            if (configuration is IConfigurationRoot root)
            {
                root.Reload();
            }

            // Hot-reload AI clients — re-read the unmasked settings from the freshly
            // saved file. GetCurrentSettings returns masked secrets, so use the DTO
            // value when it's not masked.
            var freshSettings = settingsService.GetCurrentSettings();
            var apiKey = dto.AzureAiApiKey.Contains("••••") ? null : dto.AzureAiApiKey;
            var endpoint = !string.IsNullOrWhiteSpace(dto.AzureAiEndpoint) ? dto.AzureAiEndpoint : freshSettings.AzureAiEndpoint;
            var model = !string.IsNullOrWhiteSpace(dto.AzureAiDefaultModel) ? dto.AzureAiDefaultModel : freshSettings.AzureAiDefaultModel;

            if (!string.IsNullOrWhiteSpace(endpoint) && apiKey != null)
            {
                chatClientFactory.Reconfigure(new AzureAiOptions
                {
                    Endpoint = endpoint,
                    ApiKey = apiKey,
                    DefaultModel = string.IsNullOrWhiteSpace(model) ? AzureAiOptions.DefaultModelFallback : model
                });
            }

            return Results.Ok(new { message = "Settings saved successfully" });
        });

        app.MapPost("/api/settings/test", async (
            TestConnectionRequest request,
            SettingsTestService testService,
            UserSettingsService settingsService) =>
        {
            // If the API key is masked, fill in the real stored value for testing.
            var dto = request.Settings;
            if (!string.IsNullOrEmpty(dto.AzureAiApiKey) && dto.AzureAiApiKey.Contains("••••"))
            {
                var stored = settingsService.GetRealApiKey("AzureAI:ApiKey");
                if (stored != null) dto.AzureAiApiKey = stored;
            }
            if (!string.IsNullOrEmpty(dto.DataverseClientSecret) && dto.DataverseClientSecret.Contains("••••"))
            {
                var stored = settingsService.GetRealApiKey("Dataverse:ClientSecret");
                if (stored != null) dto.DataverseClientSecret = stored;
            }
            if (!string.IsNullOrEmpty(dto.GitHubToken) && dto.GitHubToken.Contains("••••"))
            {
                var stored = settingsService.GetRealApiKey("GitHub:Token");
                if (stored != null) dto.GitHubToken = stored;
            }

            var result = await testService.TestAsync(request.Service, dto);
            return Results.Ok(result);
        });

        // Drop the cached Dataverse credential. Most useful for Interactive mode
        // (forces the next call to re-prompt the browser sign-in, letting the user
        // switch accounts). Harmless for ServicePrincipal mode — just clears the
        // token cache so the next call re-acquires.
        app.MapPost("/api/settings/dataverse/signout", (
            DataverseCredentialCache cache,
            ILogger<DataverseCredentialCache> logger) =>
        {
            cache.Reset();
            logger.LogInformation("Dataverse credential cache cleared via sign-out endpoint");
            return Results.Ok(new { message = "Signed out — next Dataverse call will require re-authentication." });
        });

        return app;
    }
}
