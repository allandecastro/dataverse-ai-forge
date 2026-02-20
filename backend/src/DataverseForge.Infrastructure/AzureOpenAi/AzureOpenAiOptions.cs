namespace DataverseForge.Infrastructure.AzureOpenAi;

public class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAI";

    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Gpt4oDeploymentName { get; set; } = "gpt-4o";
    public string Gpt4oMiniDeploymentName { get; set; } = "gpt-4o-mini";
}
