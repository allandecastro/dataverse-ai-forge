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

    // GitHub PAT — used when RagEmbeddingProvider = "GitHubModels".
    public string GitHubPersonalAccessToken { get; set; } = string.Empty;
}

public record TestConnectionRequest(string Service, SettingsDto Settings);

public record TestConnectionResult(bool Success, string Message, string? Environment = null, string? EnvironmentUrl = null);
