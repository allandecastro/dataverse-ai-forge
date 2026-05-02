namespace DataverseForge.Domain.Models;

/// <summary>
/// Lightweight representation of one option in a Dataverse option set
/// (global, local picklist, or statuscode).
/// </summary>
public record OptionInfo(
    int Value,
    string Label,
    int? StateValue = null);
