namespace DataverseForge.Domain.Models;

public partial class ProjectState
{
    // ─── SOLUTION ───
    public string SolutionUniqueName { get; set; } = string.Empty;
    public string SolutionDisplayName { get; set; } = string.Empty;
    public string SolutionVersion { get; set; } = string.Empty;
    public Guid SolutionId { get; set; }
    public bool SolutionConfirmed { get; set; }

    // ─── PUBLISHER ───
    public string PublisherDisplayName { get; set; } = string.Empty;
    public string PublisherUniqueName { get; set; } = string.Empty;
    public string PublisherPrefix { get; set; } = string.Empty;
    public int PublisherOptionValuePrefix { get; set; }
    public Guid PublisherId { get; set; }
}
