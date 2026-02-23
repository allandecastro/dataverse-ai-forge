namespace DataverseForge.Infrastructure.Settings;

public class SettingsDto
{
    // Azure OpenAI
    public string AzureOpenAiEndpoint { get; set; } = string.Empty;
    public string AzureOpenAiApiKey { get; set; } = string.Empty;
    public string AzureOpenAiGpt4oDeploymentName { get; set; } = string.Empty;
    public string AzureOpenAiGpt4oMiniDeploymentName { get; set; } = string.Empty;

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

public record TestConnectionResult(bool Success, string Message);
