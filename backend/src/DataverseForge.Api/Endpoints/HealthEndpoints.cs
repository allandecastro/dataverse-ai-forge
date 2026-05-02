using System.Reflection;
using DataverseForge.Infrastructure.Settings;

namespace DataverseForge.Api.Endpoints;

/// <summary>
/// /healthz and /readyz. /healthz probes Dataverse + Azure AI + GitHub through the
/// same code path that "Test Connection" uses, so the health badge can't disagree
/// with the Test Connection result the user just saw.
/// </summary>
internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", async (
            UserSettingsService settingsService,
            SettingsTestService testService,
            ILogger<UserSettingsService> logger) =>
        {
            // Pull the live settings (with real, unmasked secrets) and probe each service
            // through the same code path as Test Connection.
            var settings = settingsService.GetCurrentSettings();
            settings.AzureAiApiKey = settingsService.GetRealApiKey("AzureAI:ApiKey") ?? string.Empty;
            settings.DataverseClientSecret = settingsService.GetRealApiKey("Dataverse:ClientSecret") ?? string.Empty;
            settings.GitHubToken = settingsService.GetRealApiKey("GitHub:Token") ?? string.Empty;

            var checks = new Dictionary<string, object>
            {
                ["status"] = "healthy",
                ["timestamp"] = DateTime.UtcNow,
                ["version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"
            };

            // Dataverse
            if (string.IsNullOrWhiteSpace(settings.DataverseEnvironmentUrl)
                || string.IsNullOrWhiteSpace(settings.DataverseClientId)
                || string.IsNullOrWhiteSpace(settings.DataverseClientSecret)
                || settings.DataverseEnvironmentUrl.Contains("YOUR-ORG"))
            {
                checks["dataverse"] = new { status = "unavailable", configured = false };
                checks["status"] = "degraded";
            }
            else
            {
                var dv = await testService.TestAsync("dataverse", settings);
                if (dv.Success)
                {
                    checks["dataverse"] = new
                    {
                        status = "connected",
                        configured = true,
                        environment = dv.Environment,
                        environmentUrl = dv.EnvironmentUrl
                    };
                }
                else
                {
                    checks["dataverse"] = new { status = "unavailable", configured = true, error = dv.Message };
                    checks["status"] = "degraded";
                    logger.LogInformation("Health: Dataverse probe failed — {Message}", dv.Message);
                }
            }

            // Azure AI
            if (string.IsNullOrWhiteSpace(settings.AzureAiEndpoint)
                || string.IsNullOrWhiteSpace(settings.AzureAiApiKey)
                || settings.AzureAiEndpoint.Contains("YOUR-PROJECT"))
            {
                checks["azureAi"] = new { status = "unavailable", configured = false };
                checks["status"] = "degraded";
            }
            else
            {
                var ai = await testService.TestAsync("azureai", settings);
                if (ai.Success)
                {
                    checks["azureAi"] = new { status = "reachable", configured = true };
                }
                else
                {
                    checks["azureAi"] = new { status = "unavailable", configured = true, error = ai.Message };
                    checks["status"] = "degraded";
                    logger.LogInformation("Health: Azure AI probe failed — {Message}", ai.Message);
                }
            }

            // GitHub
            if (string.IsNullOrWhiteSpace(settings.GitHubToken))
            {
                checks["github"] = new { status = "unavailable", configured = false };
                checks["status"] = "degraded";
            }
            else
            {
                var gh = await testService.TestAsync("github", settings);
                if (gh.Success)
                {
                    checks["github"] = new { status = "connected", configured = true };
                }
                else
                {
                    checks["github"] = new { status = "unavailable", configured = true, error = gh.Message };
                    checks["status"] = "degraded";
                    logger.LogInformation("Health: GitHub probe failed — {Message}", gh.Message);
                }
            }

            return Results.Ok(checks);
        });

        app.MapGet("/readyz", () => Results.Ok(new { status = "ready", timestamp = DateTime.UtcNow }));

        return app;
    }
}
