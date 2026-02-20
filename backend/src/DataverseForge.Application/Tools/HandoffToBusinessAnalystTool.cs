using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToBusinessAnalystTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the Business Analyst agent for requirement elicitation. "
        + "Use this when the user's request is ambiguous, broad, or needs clarification before designing a solution. "
        + "The BA will ask clarifying questions and produce a structured requirement specification.")]
    public async Task<string> HandoffToBusinessAnalystAsync(
        [Description("Brief summary of what the user needs and why BA involvement is needed")] string reason,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.ActiveAgentName = AgentNames.BusinessAnalyst;
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        return $"Handoff to Business Analyst initiated. The Business Analyst will now gather detailed requirements. Reason: {reason}";
    }
}
