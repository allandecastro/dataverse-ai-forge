using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateFieldSecurityProfileTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateFieldSecurityProfileTool> logger)
{
    [Description("Execute the creation of a field-level security profile using a pre-built OData JSON payload from "
        + "plan_create_field_security_profile. Creates the profile, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateFieldSecurityProfileAsync(
        [Description("The full OData JSON payload from plan_create_field_security_profile")] string webApiPayloadJson,
        [Description("The profile name")] string profileName,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ExecuteCreateFieldSecurityProfile executing: profileName={ProfileName}",
            profileName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var profileId = await dataverseClient.CreateFieldSecurityProfileAsync(webApiPayloadJson, cancellationToken);

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

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SecurityBuilder,
                Action = "ExecuteCreateFieldSecurityProfile",
                Detail = $"Created field security profile '{profileName}' with ProfileId={profileId}",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateFieldSecurityProfile succeeded: profileId={ProfileId}, profileName={ProfileName}",
                profileId, profileName);

            return $"Field security profile '{profileName}' created with ProfileId={profileId}.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateFieldSecurityProfile failed: profileName={ProfileName}", profileName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SecurityBuilder,
                Action = "ExecuteCreateFieldSecurityProfile",
                Detail = $"Failed to create field security profile '{profileName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"ERROR: Failed to create field security profile '{profileName}': {ex.Message}. "
                + "Check that the payload is valid and the environment is reachable. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
