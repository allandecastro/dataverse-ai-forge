namespace DataverseForge.Domain.Models;

public record PublisherInfo(
    Guid PublisherId,
    string DisplayName,
    string UniqueName,
    string Prefix,
    int OptionValuePrefix);
