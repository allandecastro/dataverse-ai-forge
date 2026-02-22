namespace DataverseForge.Domain.Models;

public record SecurityGapAnalysis(
    IReadOnlyList<PiiFieldWithoutSecurity> PiiFieldsWithoutSecurity,
    IReadOnlyList<string> TablesWithoutCustomRoles,
    IReadOnlyList<string> OverPrivilegedRoles,
    IReadOnlyList<string> Recommendations);

public record PiiFieldWithoutSecurity(
    string TableLogicalName,
    string FieldLogicalName,
    string PiiCategory);
