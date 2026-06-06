namespace DataverseForge.Domain.Models;

/// <summary>
/// Canonical wire values for the plan-record <c>RiskLevel</c> (and the
/// workflow <c>Verdict</c>) field — a UI severity COLOUR the approval card
/// renders. This is deliberately separate from the <see cref="RiskLevel"/>
/// enum, which gauges undo danger (Low/Medium/High/Critical) on reverse ops:
/// the two are different concepts with different vocabularies.
///
/// Kept as string constants (not an enum) so the JSON sent to the frontend is
/// byte-identical to before, while a typo like "organge" now fails to compile
/// instead of shipping a silently-wrong colour.
/// </summary>
public static class PlanRisk
{
    public const string Green = "green";
    public const string Yellow = "yellow";
    public const string Orange = "orange";
    public const string Red = "red";

    /// <summary>Used by plan tools that reject a request outright.</summary>
    public const string Rejected = "rejected";
}
