namespace DataverseForge.Infrastructure.Settings;

public class SettingsDto
{
    // Azure AI (Foundry)
    public string AzureAiEndpoint { get; set; } = string.Empty;
    public string AzureAiApiKey { get; set; } = string.Empty;
    public string AzureAiDefaultModel { get; set; } = string.Empty;

    // Dataverse
    public string DataverseEnvironmentUrl { get; set; } = string.Empty;
    public string DataverseTenantId { get; set; } = string.Empty;
    public string DataverseClientId { get; set; } = string.Empty;
    public string DataverseClientSecret { get; set; } = string.Empty;

    // GitHub
    public string GitHubToken { get; set; } = string.Empty;

    // Persistence
    public string PersistenceProvider { get; set; } = string.Empty;
    public string PersistenceSqliteConnectionString { get; set; } = string.Empty;

    // OpenTelemetry
    public string OpenTelemetryOtlpEndpoint { get; set; } = string.Empty;
}

public record TestConnectionRequest(string Service, SettingsDto Settings);

public record TestConnectionResult(bool Success, string Message, string? Environment = null, string? EnvironmentUrl = null);
