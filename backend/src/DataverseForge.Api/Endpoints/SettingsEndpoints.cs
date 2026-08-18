using DataverseForge.Infrastructure;
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

        // Where the app keeps your data on disk — powers the "Data & storage"
        // panel. Reports the resolved data dir + the files that live there (with
        // sizes) so the user can see exactly what's stored locally.
        app.MapGet("/api/settings/storage", () =>
        {
            var dir = ForgePaths.GetWritableDataDir();
            var known = new[]
            {
                ("state.db", "Your projects, sessions and their built components"),
                ("rag/rag.db", "Documents you ingested for RAG context"),
                ("user-settings.json", "Connection settings (Azure AI / Dataverse). Secrets encrypted at rest"),
                (".secret.key", "Local key used to encrypt your secrets"),
            };
            var files = known
                .Select(f => (f.Item1, f.Item2, path: Path.Combine(dir, f.Item1)))
                .Where(f => File.Exists(f.path))
                .Select(f => new { name = f.Item1, description = f.Item2, sizeBytes = new FileInfo(f.path).Length })
                .ToList();

            return Results.Ok(new
            {
                dataDir = dir,
                envVar = ForgePaths.DataDirEnvVar,
                isPinnedByEnv = ForgePaths.IsPinnedByEnv,
                note = "Everything here stays on this machine. Nothing is uploaded. Only your Azure AI and Dataverse API calls leave the app.",
                files,
            });
        });

        // Change where the app stores its data. The move is deferred-and-safe:
        // we record the target (pointer + pending-move marker) and the files are
        // relocated on the NEXT startup, before any SQLite connection opens, so
        // no open DB file is ever moved. The caller must restart the app to apply.
        app.MapPost("/api/settings/storage/move", (
            MoveStorageRequest request,
            ILogger<UserSettingsService> logger) =>
        {
            if (ForgePaths.IsPinnedByEnv)
            {
                return Results.BadRequest(new
                {
                    message = $"The location is pinned by the {ForgePaths.DataDirEnvVar} environment variable. " +
                              "Unset it to choose a folder from the app.",
                });
            }

            try
            {
                var resolved = ForgePaths.RequestMove(request.Target ?? "");
                logger.LogInformation("Data dir move requested to {Target}; applies on next startup", resolved);
                return Results.Ok(new
                {
                    target = resolved,
                    message = "Location saved. Restart the app to move your data to the new folder.",
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Data dir move request failed");
                return Results.BadRequest(new { message = $"Could not use that folder: {ex.Message}" });
            }
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
                // Read per-agent overrides back from the freshly-reloaded config
                // (same source of truth as endpoint/key/model). Omitting these on
                // Reconfigure would silently drop the user's per-agent model choices.
                var agentModels = configuration.GetSection("AzureAI:AgentModels").Get<Dictionary<string, string>>() ?? new();

                chatClientFactory.Reconfigure(new AzureAiOptions
                {
                    Endpoint = aiEndpoint!,
                    ApiKey = aiApiKey!,
                    DefaultModel = string.IsNullOrWhiteSpace(aiModel) ? AzureAiOptions.DefaultModelFallback : aiModel!,
                    AgentModels = agentModels
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

            // F-45: forward HttpContext.RequestAborted so the user-cancellable
            // probe (especially the 2-min Interactive sign-in) honours an
            // aborted browser request.
            var result = await testService.TestAsync(request.Service, dto, cancellationToken);
            return Results.Ok(result);
        })
        // Names the "settings-test" fixed-window policy registered in Program.cs —
        // throttles live-credential validation so this can't be used as a probe.
        // Keep the string in sync with SettingsTestRateLimitPolicy.
        .RequireRateLimiting("settings-test");

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
            return Results.Ok(new { message = "Signed out. The next Dataverse call will require re-authentication." });
        });

        return app;
    }
}
