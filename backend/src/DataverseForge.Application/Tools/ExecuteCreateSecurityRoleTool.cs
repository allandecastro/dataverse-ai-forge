using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateSecurityRoleTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateSecurityRoleTool> logger)
{
    [Description("Execute the creation of a security role using a pre-built OData JSON payload from plan_create_security_role. "
        + "Creates the role, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateSecurityRoleAsync(
        [Description("The full OData JSON payload from plan_create_security_role")] string webApiPayloadJson,
        [Description("The role name")] string roleName,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ExecuteCreateSecurityRole executing: roleName={RoleName}",
            roleName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var roleId = await dataverseClient.CreateSecurityRoleAsync(webApiPayloadJson, cancellationToken);

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

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SecurityBuilder,
                Action = "ExecuteCreateSecurityRole",
                Detail = $"Created security role '{roleName}' with RoleId={roleId}",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateSecurityRole succeeded: roleId={RoleId}, roleName={RoleName}",
                roleId, roleName);

            return $"Security role '{roleName}' created with RoleId={roleId}.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateSecurityRole failed: roleName={RoleName}", roleName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SecurityBuilder,
                Action = "ExecuteCreateSecurityRole",
                Detail = $"Failed to create security role '{roleName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"ERROR: Failed to create security role '{roleName}': {ex.Message}. "
                + "Check that the payload is valid and the environment is reachable. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
