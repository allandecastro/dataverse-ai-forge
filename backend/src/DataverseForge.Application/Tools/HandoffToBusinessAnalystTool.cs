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
        return await HandoffTool.ExecuteHandoffAsync(projectStateStore, sessionContext, AgentNames.BusinessAnalyst, "Business Analyst", reason, cancellationToken);
    }
}
