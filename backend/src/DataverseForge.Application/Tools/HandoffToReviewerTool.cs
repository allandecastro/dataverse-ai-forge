using DataverseForge.Application.Agents;
using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToReviewerTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the Reviewer agent for pre-execution validation. "
        + "Use this when a schema design or plan needs to be validated before presenting to the user or executing. "
        + "The Reviewer will check naming, relationships, performance, security, limits, and best practices.")]
    public async Task<string> HandoffToReviewerAsync(
        [Description("The schema/plan JSON to review")] string designSpec = "",
        CancellationToken cancellationToken = default,
        [Description("ALIAS for `designSpec`. Optional. Used only when `designSpec` is empty.")] string reason = "",
        [Description("ALIAS for `designSpec`. Optional. Used only when `designSpec` and `reason` are empty.")] string summary = "")
    {
        var effective = HandoffTool.PickEffectiveSpec(designSpec, reason, summary);
        return await HandoffTool.ExecuteHandoffAsync(projectStateStore, sessionContext, AgentNames.Reviewer, "Reviewer", effective, cancellationToken);
    }
}
