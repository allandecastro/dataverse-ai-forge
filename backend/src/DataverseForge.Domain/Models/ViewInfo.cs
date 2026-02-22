namespace DataverseForge.Domain.Models;

public record ViewInfo(
    Guid ViewId,
    string Name,
    string Description,
    string FetchXml,
    string LayoutXml);
