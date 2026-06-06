using DataverseForge.Application.Agents;

namespace DataverseForge.Application.Orchestration;

/// <summary>
/// Validates handoff transitions between agents according to the orchestration topology.
/// Rules:
/// 1. SA can hand off to any agent.
/// 2. Builders (DMB, UIB, DP, SecurityBuilder, AutomationBuilder) can hand off
///    to SA or Tester — never to another builder.
/// 3. Governance agents (BA, Tester) can only hand off to SA.
///
/// The builder/governance sets live on <see cref="AgentNames"/> so this
/// validator and <c>ChainLoopDecisions.IsBuilder</c> share one definition.
/// </summary>
public static class HandoffValidator
{
    private static readonly IReadOnlySet<string> BuilderAgents = AgentNames.Builders;

    private static readonly IReadOnlySet<string> GovernanceAgents = AgentNames.Governance;

    private static readonly HashSet<string> AllowedBuilderTargets =
    [
        AgentNames.SolutionArchitect,
        AgentNames.Tester
    ];

    public static HandoffValidationResult Validate(string fromAgent, string toAgent)
    {
        // SA can hand off to anyone
        if (fromAgent == AgentNames.SolutionArchitect)
        {
            return HandoffValidationResult.Valid();
        }

        // Governance agents can only hand off to SA
        if (GovernanceAgents.Contains(fromAgent))
        {
            if (toAgent == AgentNames.SolutionArchitect)
            {
                return HandoffValidationResult.Valid();
            }

            return HandoffValidationResult.Invalid(
                $"Governance agent '{fromAgent}' can only hand off to Solution Architect, not '{toAgent}'. " +
                "All cross-agent coordination goes through SA.");
        }

        // Builders cannot hand off to other builders
        if (BuilderAgents.Contains(fromAgent))
        {
            if (AllowedBuilderTargets.Contains(toAgent))
            {
                return HandoffValidationResult.Valid();
            }

            if (BuilderAgents.Contains(toAgent))
            {
                return HandoffValidationResult.Invalid(
                    $"Builder '{fromAgent}' cannot hand off directly to builder '{toAgent}'. " +
                    "All cross-builder coordination must go through Solution Architect.");
            }

            return HandoffValidationResult.Invalid(
                $"Builder '{fromAgent}' cannot hand off to '{toAgent}'. " +
                "Builders can only hand off to Solution Architect or Tester.");
        }

        // Unknown agent — allow (don't block unexpected agents)
        return HandoffValidationResult.Valid();
    }
}

public record HandoffValidationResult(bool IsValid, string? ErrorMessage)
{
    public static HandoffValidationResult Valid() => new(true, null);
    public static HandoffValidationResult Invalid(string message) => new(false, message);
}
