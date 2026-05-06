using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Lists every security role in the environment, including BOTH managed
/// (System Administrator, Sales Person, etc.) and unmanaged custom roles.
/// Read-only. Used by UIBuilder to look up a role's id before
/// <c>assign_app_role</c>, by SolutionArchitect to answer "what roles
/// exist?" questions directly, and (later) by Security Builder.
/// </summary>
public class GetSecurityRolesTool(
    IDataverseClient dataverseClient,
    ILogger<GetSecurityRolesTool> logger)
{
    [Description("List security roles in the Dataverse environment. Returns roleid, name, "
        + "description, business unit, IsManaged, IsAutoAssigned, AppliesTo. Includes BOTH "
        + "managed and unmanaged roles — most useful OOB roles (System Administrator, Sales "
        + "Person, etc.) are managed. Optional searchTerm does a case-insensitive "
        + "name-contains match. Optional businessUnitId filters to a single BU. Read-only.")]
    public async Task<SecurityRolesResult> GetSecurityRolesAsync(
        [Description("Optional name search (case-insensitive contains). Leave empty/null for all roles.")]
        string? searchTerm,
        [Description("Optional business unit ID (GUID) to filter by. Leave empty/null for all BUs.")]
        string? businessUnitId,
        CancellationToken cancellationToken)
    {
        Guid? buGuid = null;
        if (!string.IsNullOrWhiteSpace(businessUnitId))
        {
            if (Guid.TryParse(businessUnitId, out var parsed) && parsed != Guid.Empty)
            {
                buGuid = parsed;
            }
            // Otherwise silently ignore — the agent sometimes passes the BU
            // name by mistake; better to surface "no roles with that filter"
            // than block the call.
        }

        try
        {
            var roles = await dataverseClient.GetSecurityRolesAsync(searchTerm, buGuid, cancellationToken);
            return new SecurityRolesResult(roles, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "GetSecurityRoles failed (searchTerm={SearchTerm}, businessUnitId={BusinessUnitId})",
                searchTerm, buGuid);
            return new SecurityRolesResult(
                Array.Empty<SecurityRoleInfo>(),
                $"ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>
/// Wrapper for <see cref="GetSecurityRolesTool"/>'s return value. Surfaces
/// the underlying Dataverse error verbatim so the agent can quote it back
/// to the user instead of seeing the AI framework's generic "Function
/// failed" wrapper.
/// </summary>
public sealed record SecurityRolesResult(
    IReadOnlyList<SecurityRoleInfo> Roles,
    string? Error);
