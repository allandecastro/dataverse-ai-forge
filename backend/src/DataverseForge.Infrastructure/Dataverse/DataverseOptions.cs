namespace DataverseForge.Infrastructure.Dataverse;

public sealed class DataverseOptions
{
    public const string SectionName = "Dataverse";

    public string EnvironmentUrl { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
