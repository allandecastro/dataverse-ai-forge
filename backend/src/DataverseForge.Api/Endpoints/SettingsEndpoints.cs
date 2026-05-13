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
            IConfiguration configuration,
            ILogger<ChatClientFactory> logger) =>
        {
            await settingsService.SaveSettingsAsync(dto);

            // Force-reload configuration so IOptionsMonitor.CurrentValue picks up
            // new values immediately (no debounce wait).
            if (configuration is IConfigurationRoot root)
            {
                root.Reload();
            }

            // Hot-reload AI clients from the freshly-saved file — never from the DTO.
            // The DTO can carry empty strings for fields the user didn't touch (most
            // commonly the password input rendered blank for the masked secret), and
            // an empty-string ApiKey would slip past `apiKey != null` and trigger
            // Reconfigure with invalid credentials, which silently wipes the factory
            // (_configured=false) even though SaveSettingsAsync correctly preserved
            // the real value on disk via SetIfNotMasked. The file is the single
            // source of truth post-Reload — read from it.
            var aiEndpoint = configuration["AzureAI:Endpoint"];
            var aiApiKey = configuration["AzureAI:ApiKey"];
            var aiModel = configuration["AzureAI:DefaultModel"];

            if (!string.IsNullOrWhiteSpace(aiEndpoint) && !string.IsNullOrWhiteSpace(aiApiKey))
            {
                chatClientFactory.Reconfigure(new AzureAiOptions
                {
                    Endpoint = aiEndpoint!,
                    ApiKey = aiApiKey!,
                    DefaultModel = string.IsNullOrWhiteSpace(aiModel) ? AzureAiOptions.DefaultModelFallback : aiModel!
                });
            }
            else
            {
                logger.LogInformation(
                    "Settings saved but Azure AI section is incomplete (endpoint or key missing) — ChatClientFactory not reconfigured");
            }

            return Results.Ok(new { message = "Settings saved successfully" });
        });

        app.MapPost("/api/settings/test", async (
            TestConnectionRequest request,
            SettingsTestService testService,
            UserSettingsService settingsService,
            CancellationToken cancellationToken) =>
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

            // F-45: forward HttpContext.RequestAborted so the user-cancellable
            // probe (especially the 2-min Interactive sign-in) honours an
            // aborted browser request.
            var result = await testService.TestAsync(request.Service, dto, cancellationToken);
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
