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
}
