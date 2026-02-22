namespace DataverseForge.Domain.Models;

public record FormInfo(
    Guid FormId,
    string Name,
    string FormType,
    string Description,
    bool IsDefault);

public record FormDefinition(
    Guid FormId,
    string Name,
    string FormType,
    string FormXml);
