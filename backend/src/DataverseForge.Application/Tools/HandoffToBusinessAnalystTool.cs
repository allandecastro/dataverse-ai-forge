using DataverseForge.Application.Agents;
using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToBusinessAnalystTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the Business Analyst agent for requirement elicitation. "
        + "Use this ONLY when starting a NEW broad feature where the requirement isn't yet specified — "
        + "e.g. user says 'I want to manage gifts', 'build a recruiting solution'. "
        + "DO NOT use this for: troubleshooting build/test failures, follow-ups on already-specced features, "
        + "specific schema/UI instructions ('add a phone field'), or when the user just told you to 'go back', "
        + "'stay', 'diagnose', or 'review'. BA gathers brand-new requirements; she does not debug or own UX iteration.")]
    public async Task<string> HandoffToBusinessAnalystAsync(
        [Description("Brief summary of what the user needs and why BA involvement is needed")] string reason,
        CancellationToken cancellationToken)
    {
        return await HandoffTool.ExecuteHandoffAsync(projectStateStore, sessionContext, AgentNames.BusinessAnalyst, "Business Analyst", reason, cancellationToken);
    }
}
