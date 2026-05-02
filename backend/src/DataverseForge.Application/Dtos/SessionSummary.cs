namespace DataverseForge.Application.Dtos;

public record SessionSummary(
    string SessionId,
    string ProjectName,
    string EnvironmentUrl,
    string SolutionName,
    string ActiveAgent,
    int TableCount,
    int FieldCount,
    DateTime CreatedAt,
    DateTime LastActivityAt);
