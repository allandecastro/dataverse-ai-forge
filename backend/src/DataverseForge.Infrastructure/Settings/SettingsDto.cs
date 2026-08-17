namespace DataverseForge.Infrastructure.Settings;

public class SettingsDto
{
    // Azure AI (Foundry)
    public string AzureAiEndpoint { get; set; } = string.Empty;
    public string AzureAiApiKey { get; set; } = string.Empty;
    public string AzureAiDefaultModel { get; set; } = string.Empty;

    /// <summary>
    /// Optional per-agent model overrides (key = agent name, value = model
    /// deployment name). An agent absent from the map, or with an empty value,
    /// uses <see cref="AzureAiDefaultModel"/>. Nullable on purpose: a PUT that
    /// omits the field leaves any existing overrides untouched, whereas an
    /// explicit (possibly empty) object rewrites the whole map — so clearing an
    /// override in the UI actually removes it.
    /// </summary>
    public Dictionary<string, string>? AzureAiAgentModels { get; set; }

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
}

public record TestConnectionRequest(string Service, SettingsDto Settings);

public record TestConnectionResult(bool Success, string Message, string? Environment = null, string? EnvironmentUrl = null);

/// <summary>Body for POST /api/settings/storage/move — the absolute folder the
/// user picked to relocate the app's data directory to.</summary>
public record MoveStorageRequest(string Target);
