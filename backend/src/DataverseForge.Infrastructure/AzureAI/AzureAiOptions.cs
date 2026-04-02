namespace DataverseForge.Infrastructure.AzureAI;

public class AzureAiOptions
{
    public const string SectionName = "AzureAI";

    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = "gpt-4o";

    /// <summary>
    /// Per-agent model mapping. Key = agent name (e.g., "SolutionArchitect"),
    /// Value = model deployment name (e.g., "gpt-4o", "gpt-4o-mini", "llama-3.3-70b").
    /// Agents not listed here use <see cref="DefaultModel"/>.
    /// </summary>
    public Dictionary<string, string> AgentModels { get; set; } = new();

    /// <summary>
    /// Resolve the model name for a given agent. Falls back to <see cref="DefaultModel"/>.
    /// </summary>
    public string GetModelForAgent(string agentName) =>
        AgentModels.GetValueOrDefault(agentName, DefaultModel);
}
