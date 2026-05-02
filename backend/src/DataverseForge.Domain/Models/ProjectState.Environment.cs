namespace DataverseForge.Domain.Models;

public partial class ProjectState
{
    // ─── ENVIRONMENT ───
    public string EnvironmentUrl { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public int BaseLanguageLcid { get; set; }
    public string DataverseVersion { get; set; } = string.Empty;
    public bool EnvironmentConfirmed { get; set; }
}
