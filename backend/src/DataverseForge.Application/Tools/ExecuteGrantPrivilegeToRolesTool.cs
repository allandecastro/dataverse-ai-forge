using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteGrantPrivilegeToRolesTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteGrantPrivilegeToRolesTool> logger)
{
    [Description("Execute a bulk privilege grant. Loops AddPrivilegesRole once per RoleId from "
        + "the staged plan. A per-role failure surfaces as a PARTIAL result with the failed "
        + "roleIds verbatim — other roles in the batch still get the privilege. "
        + "IsReversible=false in v1: undo would need a per-role pre-edit snapshot which the "
        + "planner doesn't capture. Manual rollback via plan_update_security_role per affected "
        + "role.")]
    public async Task<string> ExecuteGrantPrivilegeToRolesAsync(
        [Description("planId returned by plan_grant_privilege_to_roles — must match exactly")]
        string planId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteGrantPrivilegeToRoles executing: planId={PlanId}", planId);

        if (planId.StartsWith("rejected_bulk_grant_", StringComparison.Ordinal))
        {
            logger.LogInformation("ExecuteGrantPrivilegeToRoles: skipping rejected plan: {PlanId}", planId);
            return $"SKIPPED: Plan '{planId}' was rejected by plan_grant_privilege_to_roles. "
                + "Re-run the planner with corrected inputs.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var lookupStatus = state.LookupPlan(planId, out var compositePayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteGrantPrivilegeToRoles: planId {PlanId} already executed", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier in this "
                + "session. DO NOT retry. Call plan_grant_privilege_to_roles for a new bulk "
                + "grant if the user wants another change.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteGrantPrivilegeToRoles: planId not found: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_grant_privilege_to_roles first.";
        }

        var doc = JsonDocument.Parse(compositePayloadJson);
        var privEl = doc.RootElement.GetProperty("privilege");
        var privilegeId = privEl.GetProperty("PrivilegeId").GetGuid();
        var depth = privEl.GetProperty("Depth").GetString() ?? "Basic";
        var privilegeName = privEl.TryGetProperty("Name", out var nm) ? nm.GetString() : null;

        var roleIds = doc.RootElement.GetProperty("roleIds")
            .EnumerateArray()
            .Select(e => e.GetGuid())
            .ToList();

        var assignment = new RolePrivilegeAssignment(privilegeId, depth, privilegeName);
        var perRolePrivileges = new[] { assignment };

        var succeeded = new List<Guid>();
        var failures = new List<(Guid roleId, string error)>();
        foreach (var roleId in roleIds)
        {
            try
            {
                await dataverseClient.AddPrivilegesToRoleAsync(roleId, perRolePrivileges, cancellationToken);
                succeeded.Add(roleId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ExecuteGrantPrivilegeToRoles: AddPrivilegesRole failed for role {RoleId}", roleId);
                failures.Add((roleId, ex.Message));
            }
        }

        var allSucceeded = failures.Count == 0;

        await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
        {
            fresh.ExecutedPlans[planId] = compositePayloadJson;
            fresh.PendingPlans.Remove(planId);
            fresh.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.SecurityBuilder,
                ToolName = "ExecuteGrantPrivilegeToRoles",
                Parameters = new { privilegeName, depth, roleCount = roleIds.Count, planId },
                Result = new { planId, succeededCount = succeeded.Count, failedCount = failures.Count },
                IsReversible = false,
                ReverseOp = null,
            });
            fresh.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SecurityBuilder,
                Action = "ExecuteGrantPrivilegeToRoles",
                Detail = allSucceeded
                    ? $"Granted {privilegeName ?? privilegeId.ToString()} @ {depth} to {succeeded.Count} role(s)"
                    : $"PARTIAL: granted to {succeeded.Count}/{roleIds.Count}; failures: "
                        + string.Join(", ", failures.Select(f => f.roleId)),
                Success = allSucceeded,
            });
        }, cancellationToken);

        sw.Stop();
        ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("tool.name", "ExecuteGrantPrivilegeToRoles"),
            new KeyValuePair<string, object?>("tool.status", allSucceeded ? "success" : "partial"));

        if (allSucceeded)
        {
            return $"Granted {privilegeName ?? privilegeId.ToString()} @ {depth} to "
                + $"{succeeded.Count} role(s). "
                + $"NEXT REQUIRED ACTION: call handoff_to_tester with planId={planId} so the "
                + "Tester can verify every role got the privilege at the requested depth.";
        }

        return $"PARTIAL: Granted to {succeeded.Count}/{roleIds.Count} role(s). "
            + $"Failures: {string.Join(" | ", failures.Select(f => $"{f.roleId}: {f.error}"))}. "
            + $"NEXT REQUIRED ACTION: surface the failure list to the user verbatim; re-plan "
            + $"plan_grant_privilege_to_roles with only the failed roleIds to retry, OR call "
            + $"plan_update_security_role per failed role for a per-role fix.";
    }
}
