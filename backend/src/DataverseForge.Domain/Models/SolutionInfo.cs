namespace DataverseForge.Domain.Models;

public record SolutionInfo(
    Guid SolutionId,
    string UniqueName,
    string DisplayName,
    string Version,
    string PublisherName,
    Guid PublisherId,
    string PublisherUniqueName,
    string PublisherPrefix,
    bool IsManaged,
    int ComponentCount,
    DateTime ModifiedOn);
