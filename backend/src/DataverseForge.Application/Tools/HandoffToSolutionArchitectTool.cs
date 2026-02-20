using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToSolutionArchitectTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Hand off the conversation back to the Solution Architect with a structured requirement specification. "
        + "Use this when you have gathered enough requirements and produced a clear, actionable spec. "
        + "Always include the structured requirements in the requirementSpec parameter.")]
    public async Task<string> HandoffToSolutionArchitectAsync(
        [Description("The structured requirement specification to hand off to the Architect")] string requirementSpec,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.ActiveAgentName = AgentNames.SolutionArchitect;
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        return $"Handoff to Solution Architect initiated with requirement specification.";
    }
}
