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

    // RunId of the turn in which the most recent handoff_to_* was issued. Used
    // by HandoffTool to enforce one-handoff-per-turn at the code layer (the
    // prompt rule alone wasn't being followed; the agent kept emitting parallel
    // HandoffToUIBuilder + HandoffToTester calls and Tester ran on a build that
    // hadn't happened yet).
    public string LastHandoffRunId { get; set; } = string.Empty;

    // ─── CONVENIENCE ───
    public bool IsReady => EnvironmentConfirmed && SolutionConfirmed;
    public string FullPrefix => $"{PublisherPrefix}_";
}
