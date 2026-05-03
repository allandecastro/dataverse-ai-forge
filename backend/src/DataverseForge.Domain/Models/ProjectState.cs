namespace DataverseForge.Domain.Models;

/// <summary>
/// Per-session aggregate. Split into partial classes by concern (see ProjectState.*.cs):
///   - Identity / Session   — ProjectId, ActiveAgent, timestamps
///   - Environment          — env URL, version, language
///   - Solution / Publisher — active solution + publisher metadata
///   - Schema               — created tables / fields / relationships / etc.
///   - Plans                — pending and executed OData payloads keyed by planId
///   - Audit                — audit log + operation stack for undo
/// We keep one flat class (no nested objects) so SQLite serialization stays unchanged
/// and existing callsites (state.PendingPlans, state.Tables, …) keep working.
/// </summary>
public partial class ProjectState
{
    // ─── IDENTITY / SESSION ───
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ActiveAgentName { get; set; } = "SolutionArchitect";
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }

    // (RunId, FromAgent) of the most recent handoff_to_* call. Used by
    // HandoffTool to enforce ONE handoff PER AGENT PER TURN (not one handoff
    // total per turn). The original blanket rule rejected legitimate routing
    // chains — UIBuilder → SA → DMB is three different agents each issuing one
    // handoff, all in the same turn, and that's correct. The bug we're
    // guarding is the SAME agent firing handoff_to_X + handoff_to_Y in
    // parallel (e.g. SA emitting HandoffToUIBuilder + HandoffToTester in one
    // response, racing the ActiveAgentName field).
    public string LastHandoffRunId { get; set; } = string.Empty;
    public string LastHandoffFromAgent { get; set; } = string.Empty;

    // ─── CONVENIENCE ───
    public bool IsReady => EnvironmentConfirmed && SolutionConfirmed;
    public string FullPrefix => $"{PublisherPrefix}_";
}
