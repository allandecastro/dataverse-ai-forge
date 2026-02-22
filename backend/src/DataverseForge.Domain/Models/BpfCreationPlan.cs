namespace DataverseForge.Domain.Models;

public record BpfCreationPlan(
    string BpfName,
    string DisplayName,
    string Description,
    string PrimaryTable,
    IReadOnlyList<BpfStage> Stages,
    string StageFlowDiagram,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

public record BpfStage(
    string Name,
    string DisplayName,
    string Category,
    string? CrossEntityTable,
    IReadOnlyList<BpfStageStep> Steps);

public record BpfStageStep(
    string FieldLogicalName,
    bool IsRequired,
    int SequenceNumber);
