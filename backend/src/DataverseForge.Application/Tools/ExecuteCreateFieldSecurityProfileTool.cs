using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateFieldSecurityProfileTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a field-level security profile using a pre-built OData JSON payload from "
        + "plan_create_field_security_profile. Creates the profile, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateFieldSecurityProfileAsync(
        [Description("The full OData JSON payload from plan_create_field_security_profile")] string webApiPayloadJson,
        [Description("The profile name")] string profileName,
        CancellationToken cancellationToken)
    {
        var profileId = await dataverseClient.CreateFieldSecurityProfileAsync(webApiPayloadJson, cancellationToken);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        // Track as a security role variant (field security profiles are part of the security model)
        state.SecurityRoles.Add(new CreatedSecurityRole(profileId, $"[FieldSecurity] {profileName}", DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.SecurityBuilder,
            ToolName = "ExecuteCreateFieldSecurityProfile",
            Parameters = new { profileName },
            Result = new { profileId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteFieldSecurityProfile",
                Parameters = new { profileId, profileName },
                Warning = "Deleting a field security profile removes field-level access restrictions for all assigned users.",
                RiskLevel = RiskLevel.High
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"Field security profile '{profileName}' created with ProfileId={profileId}.";
    }
}
