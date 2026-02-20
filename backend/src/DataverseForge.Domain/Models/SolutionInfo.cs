namespace DataverseForge.Domain.Models;

public record SolutionInfo(
    Guid SolutionId,
    string UniqueName,
    string DisplayName,
    string Version,
    string PublisherName,
    bool IsManaged,
    int ComponentCount,
    DateTime ModifiedOn);
