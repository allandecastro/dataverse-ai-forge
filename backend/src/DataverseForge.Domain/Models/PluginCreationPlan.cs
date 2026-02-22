namespace DataverseForge.Domain.Models;

public record PluginCreationPlan(
    string PluginName,
    string TableLogicalName,
    string Message,
    int Stage,
    string ExecutionMode,
    string Description,
    string CSharpCode,
    IReadOnlyList<string> RegistrationSteps,
    IReadOnlyList<string> NuGetReferences,
    string UnitTestCode,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
