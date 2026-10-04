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

    // Azure DevOps — used by AzureDevOpsDocumentSource for RAG ingestion of the backlog.
    public string AzureDevOpsOrganizationUrl { get; set; } = string.Empty;
    public string AzureDevOpsProject { get; set; } = string.Empty;
    public string AzureDevOpsPersonalAccessToken { get; set; } = string.Empty;

    // RAG — embedding provider for the knowledge-base pipeline.
    // "AzureOpenAI"  : uses the AzureAI endpoint + key already configured above.
    // "GitHubModels" : uses https://models.inference.ai.azure.com with a GitHub PAT.
    //                  Set AzureAiApiKey to your GitHub PAT and leave AzureAiEndpoint empty.
    //                  The IEmbeddingGenerator registered in DependencyInjection.Rag.cs
    //                  points to the GitHub Models endpoint automatically.
    // "LocalOnnx"    : reserved for future offline ONNX embedding (no network required).
    public string RagEmbeddingProvider { get; set; } = "AzureOpenAI";

    // Embedding deployment (Azure OpenAI) or model name (GitHub Models).
    // Empty default so a client that doesn't send it (the current frontend) leaves the
    // saved value untouched instead of resetting it; DependencyInjection.Rag falls back
    // to text-embedding-3-small when unset.
    public string RagEmbeddingDeployment { get; set; } = string.Empty;

    // GitHub PAT — used when RagEmbeddingProvider = "GitHubModels".
    public string GitHubPersonalAccessToken { get; set; } = string.Empty;
}

public record TestConnectionRequest(string Service, SettingsDto Settings);

public record TestConnectionResult(bool Success, string Message, string? Environment = null, string? EnvironmentUrl = null);

/// <summary>Body for POST /api/settings/storage/move — the absolute folder the
/// user picked to relocate the app's data directory to.</summary>
public record MoveStorageRequest(string Target);
