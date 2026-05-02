namespace DataverseForge.Domain.Models;

/// <summary>
/// Summary of a global option set in the environment — used by the DMB to
/// decide whether to reuse an existing global option set or create a new one.
/// </summary>
public record GlobalOptionSetSummary(
    string Name,
    string DisplayName,
    bool IsCustom,
    bool IsManaged,
    IReadOnlyList<OptionInfo> Options);
