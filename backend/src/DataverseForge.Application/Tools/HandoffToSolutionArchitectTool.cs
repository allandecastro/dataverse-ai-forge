using DataverseForge.Application.Agents;
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
        [Description("The structured requirement specification to hand off to the Architect")] string requirementSpec = "",
        CancellationToken cancellationToken = default,
        [Description("ALIAS for `requirementSpec`. Optional. Used only when `requirementSpec` is empty.")] string reason = "",
        [Description("ALIAS for `requirementSpec`. Optional. Used only when `requirementSpec` and `reason` are empty.")] string summary = "")
    {
        var effective = HandoffTool.PickEffectiveSpec(requirementSpec, reason, summary);
        return await HandoffTool.ExecuteHandoffAsync(projectStateStore, sessionContext, AgentNames.SolutionArchitect, "Solution Architect", effective, cancellationToken);
    }
}
