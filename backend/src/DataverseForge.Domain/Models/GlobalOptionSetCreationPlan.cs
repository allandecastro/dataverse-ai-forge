namespace DataverseForge.Domain.Models;

public record GlobalOptionSetCreationPlan(
    string Name,
    string DisplayName,
    string Description,
    string OptionSetType,
    IReadOnlyList<OptionValueEntry> Options,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

public record OptionValueEntry(
    int Value,
    string Label);
