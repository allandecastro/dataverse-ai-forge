namespace DataverseForge.Application.Agents;

/// <summary>
/// Canonical names for the active agents. Used as keys in the orchestration
/// dispatcher, in handoff topology rules, in audit-log entries, and as the
/// "agent" label on tool calls.
///
/// Lives in Application (next to the agent classes) — Domain has zero usages,
/// and Application is the boundary that owns the concept of "an agent".
/// </summary>
public static class AgentNames
{
    public const string SolutionArchitect = "SolutionArchitect";
    public const string BusinessAnalyst = "BusinessAnalyst";
    public const string DataModelBuilder = "DataModelBuilder";
    public const string Tester = "Tester";
    public const string UIBuilder = "UIBuilder";
    public const string DataPopulation = "DataPopulation";
    public const string SecurityBuilder = "SecurityBuilder";
    public const string AutomationBuilder = "AutomationBuilder";

    /// <summary>
    /// Builder agents — those that perform Dataverse write work and must route
    /// all cross-builder coordination through Solution Architect (never hand off
    /// directly to another builder). Single source of truth for the handoff
    /// topology: <see cref="Orchestration.HandoffValidator"/> and
    /// <c>ChainLoopDecisions.IsBuilder</c> both consume this so the two cannot
    /// drift (they previously disagreed — Security/Automation builders were
    /// missing from the validator, leaving a builder→builder handoff hole).
    /// </summary>
    public static readonly IReadOnlySet<string> Builders = new HashSet<string>
    {
        DataModelBuilder,
        UIBuilder,
        DataPopulation,
        SecurityBuilder,
        AutomationBuilder
    };

    public const string DocumentaryAgent = "DocumentaryAgent";

    /// <summary>
    /// Governance agents — read/advisory only; can only hand off back to
    /// Solution Architect (or to DocumentaryAgent for BA).
    /// </summary>
    public static readonly IReadOnlySet<string> Governance = new HashSet<string>
    {
        BusinessAnalyst,
        Tester
    };

    /// <summary>
    /// Service agents — perform a single task then always return to SA.
    /// Forced-SA-handoff logic in ChainLoopDecisions applies to all of them.
    /// </summary>
    public static readonly IReadOnlySet<string> ServiceAgents = new HashSet<string>
    {
        DocumentaryAgent
    };

    /// <summary>
    /// Every canonical agent name. Single source of truth for validation (e.g.
    /// catching typo'd keys in <c>AzureAI:AgentModels</c> config) so callers
    /// don't hand-roll the full list.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SolutionArchitect,
        BusinessAnalyst,
        DataModelBuilder,
        Tester,
        UIBuilder,
        DataPopulation,
        SecurityBuilder,
        AutomationBuilder,
        DocumentaryAgent
    };
}
