namespace DataverseForge.Domain.Models;

public record PluginProjectPlan(
    string ProjectName,
    string SolutionName,
    string TargetFramework,
    IReadOnlyList<PluginProjectFile> Files,
    string CsprojContent,
    string BuildInstructions,
    IReadOnlyList<string> Warnings);

public record PluginProjectFile(
    string Path,
    string Content);
