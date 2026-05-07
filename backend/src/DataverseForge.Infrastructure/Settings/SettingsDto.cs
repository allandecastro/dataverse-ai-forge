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

    /// <summary>
    /// "ServicePrincipal" (default) or "Interactive". String rather than enum
    /// to keep the DTO JSON stable across versions if the enum gains values.
    /// </summary>
    public string DataverseAuthMode { get; set; } = "ServicePrincipal";

    // GitHub
    public string GitHubToken { get; set; } = string.Empty;
}

public record TestConnectionRequest(string Service, SettingsDto Settings);

public record TestConnectionResult(bool Success, string Message, string? Environment = null, string? EnvironmentUrl = null);
