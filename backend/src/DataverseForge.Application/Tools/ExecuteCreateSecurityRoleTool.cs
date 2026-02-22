using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateSecurityRoleTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a security role using a pre-built OData JSON payload from plan_create_security_role. "
        + "Creates the role, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateSecurityRoleAsync(
        [Description("The full OData JSON payload from plan_create_security_role")] string webApiPayloadJson,
        [Description("The role name")] string roleName,
        CancellationToken cancellationToken)
    {
        var roleId = await dataverseClient.CreateSecurityRoleAsync(webApiPayloadJson, cancellationToken);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.SecurityRoles.Add(new CreatedSecurityRole(roleId, roleName, DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.SecurityBuilder,
            ToolName = "ExecuteCreateSecurityRole",
            Parameters = new { roleName },
            Result = new { roleId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteSecurityRole",
                Parameters = new { roleId, roleName },
                Warning = "Deleting a security role removes access for all users assigned to it.",
                RiskLevel = RiskLevel.High
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"Security role '{roleName}' created with RoleId={roleId}.";
    }
}
