namespace DataverseForge.Domain.Models;

public record BpfMetadata(
    Guid ProcessId,
    string Name,
    string PrimaryTable,
    string Category,
    int StateCode,
    int StatusCode);
