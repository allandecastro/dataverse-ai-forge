using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToSecurityBuilderTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the Security Builder agent to design and implement "
        + "security roles, field-level security profiles, and column security. "
        + "Use this when you need to implement the security model for the solution.")]
    public async Task<string> HandoffToSecurityBuilderAsync(
        [Description("The security requirements or specification to implement")] string securitySpec,
        CancellationToken cancellationToken)
    {
        return await HandoffTool.ExecuteHandoffAsync(projectStateStore, sessionContext, AgentNames.SecurityBuilder, "Security Builder", securitySpec, cancellationToken);
    }
}
