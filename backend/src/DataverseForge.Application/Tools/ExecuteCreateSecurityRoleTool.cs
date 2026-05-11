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

public class ExecuteCreateSecurityRoleTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateSecurityRoleTool> logger)
{
    [Description("Execute the creation of a Dataverse security role. Pass the planId from "
        + "plan_create_security_role — the role POST body and AddPrivilegesRole action body were "
        + "stored server-side and are loaded automatically. Do NOT try to construct payloads "
        + "yourself. Returns the new RoleId.")]
    public async Task<string> ExecuteCreateSecurityRoleAsync(
        [Description("planId returned by plan_create_security_role — must match exactly")] string planId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateSecurityRole executing: planId={PlanId}", planId);

        // Plans rejected by the planner (duplicate name / invalid spec / missing
        // privileges) carry a known prefix and never reach PendingPlans.
        // Returning a clear SKIPPED message is cheaper than letting the agent
        // try to execute and bounce off a NotFound.
        if (planId.StartsWith("rejected_role_exists_", StringComparison.Ordinal))
        {
            logger.LogInformation("ExecuteCreateSecurityRole: skipping rejected (duplicate-name) plan: {PlanId}", planId);
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_security_role because "
                + "the role already exists. Use plan_update_security_role if a privilege change "
                + "is needed.";
        }
        if (planId.StartsWith("rejected_role_invalid_", StringComparison.Ordinal))
        {
            logger.LogInformation("ExecuteCreateSecurityRole: skipping rejected (invalid-spec) plan: {PlanId}", planId);
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_security_role because "
                + "the spec was invalid. Re-run plan_create_security_role with corrected inputs.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var lookupStatus = state.LookupPlan(planId, out var compositePayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteCreateSecurityRole: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier in this session. "
                + "DO NOT retry, DO NOT re-plan. If the user is asking for a different role now, "
                + "call plan_create_security_role with the new spec.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteCreateSecurityRole: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_security_role first and "
                + "pass its returned planId verbatim.";
        }

        // Decode the composite payload — { role: {...}, privileges: [{PrivilegeId, Depth, Name}, ...] }.
        var doc = JsonDocument.Parse(compositePayloadJson);
        var rolePayloadJson = doc.RootElement.GetProperty("role").GetRawText();

        var privileges = new List<RolePrivilegeAssignment>();
        if (doc.RootElement.TryGetProperty("privileges", out var privArray) &&
            privArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in privArray.EnumerateArray())
            {
                var id = p.GetProperty("PrivilegeId").GetGuid();
                var depth = p.GetProperty("Depth").GetString() ?? "Basic";
                var name = p.TryGetProperty("Name", out var n) ? n.GetString() : null;
                privileges.Add(new RolePrivilegeAssignment(id, depth, name));
            }
        }

        // Capture the role's display name + business-unit id from the payload
        // for the post-execute log line and the ExecutedOperation record.
        var roleName = doc.RootElement.GetProperty("role").GetProperty("name").GetString() ?? string.Empty;

        Guid newRoleId;
        try
        {
            newRoleId = await dataverseClient.CreateRoleAsync(rolePayloadJson, cancellationToken);
            logger.LogInformation(
                "Role record created: roleId={RoleId} name={Name} privCount={Count}",
                newRoleId, roleName, privileges.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExecuteCreateSecurityRole: CreateRole failed for {Name}", roleName);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.SecurityBuilder,
                    Action = "ExecuteCreateSecurityRole",
                    Detail = $"Failed to create role '{roleName}': {ex.Message}",
                    Success = false
                });
            }, cancellationToken);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateSecurityRole"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create role '{roleName}': {ex.Message}";
        }

        // Phase 2: attach privileges. If THIS step fails the role row exists
        // but is empty — surface that explicitly so the agent can either undo
        // (delete + recreate, once Commit 3 ships delete) or re-attempt the
        // privilege attach via plan_update_security_role.
        var privilegeError = string.Empty;
        if (privileges.Count > 0)
        {
            try
            {
                await dataverseClient.AddPrivilegesToRoleAsync(newRoleId, privileges, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ExecuteCreateSecurityRole: AddPrivilegesRole failed for role {RoleId}", newRoleId);
                privilegeError = $" WARNING: role record was created (RoleId={newRoleId}) but the "
                    + $"privilege attach failed: {ex.Message}. The role exists with NO privileges. "
                    + "Use plan_update_security_role to re-attempt the privilege grant.";
            }
        }

        // Solution-tracking: add the role to the active solution so it ships
        // with the export. Best-effort — the role exists in the env regardless.
        var solutionUniqueName = state.SolutionUniqueName;
        var solutionAddNote = string.Empty;
        if (!string.IsNullOrWhiteSpace(solutionUniqueName))
        {
            try
            {
                await dataverseClient.AddSolutionComponentAsync(
                    newRoleId, SolutionComponentType.Role, solutionUniqueName,
                    doNotIncludeSubcomponents: false, cancellationToken);
                solutionAddNote = $" Added to solution '{solutionUniqueName}'.";
            }
            catch (Exception solEx)
            {
                logger.LogWarning(solEx,
                    "AddSolutionComponent failed for role {RoleId} ({Name}) on solution {Solution}",
                    newRoleId, roleName, solutionUniqueName);
                solutionAddNote = $" WARNING: role created in env but could NOT be added to "
                    + $"solution '{solutionUniqueName}': {solEx.Message}.";
            }
        }
        else
        {
            solutionAddNote = " WARNING: no active solution selected — the role is in the default "
                + "solution. Use select_solution + add_existing_component_to_solution if it should "
                + "ship in a specific solution.";
        }

        // F-23: re-read state inside the apply so concurrent writes during the
        // ~5-30s of Dataverse round-trips don't get clobbered.
        await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
        {
            fresh.ExecutedPlans[planId] = compositePayloadJson;
            fresh.PendingPlans.Remove(planId);
            fresh.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.SecurityBuilder,
                ToolName = "ExecuteCreateSecurityRole",
                Parameters = new { roleName, privilegeCount = privileges.Count, planId },
                Result = new { roleId = newRoleId, planId },
                // Commit 1 stages the reverse-op metadata but leaves IsReversible=false
                // because the undo dispatcher doesn't know how to handle it yet.
                // Commit 3 lands the DeleteSecurityRole undo case + flips this flag.
                // This matches the F-25 follow-through pattern from commit 456c724.
                IsReversible = false,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteSecurityRole",
                    Parameters = new { roleId = newRoleId, roleName },
                    Warning = "Recreating the role does NOT restore its privilege matrix; the new "
                        + "role gets a fresh GUID and zero privileges. Re-attach via "
                        + "plan_update_security_role if needed.",
                    RiskLevel = RiskLevel.High
                }
            });
            fresh.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SecurityBuilder,
                Action = "ExecuteCreateSecurityRole",
                Detail = $"Created role '{roleName}' (RoleId={newRoleId}) with {privileges.Count} privilege(s)",
                Success = true
            });
        }, cancellationToken);

        sw.Stop();
        ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateSecurityRole"), new KeyValuePair<string, object?>("tool.status", "success"));

        return $"Role '{roleName}' created with RoleId={newRoleId} and {privileges.Count} "
            + $"privilege(s) attached.{solutionAddNote}{privilegeError} "
            + $"NEXT REQUIRED ACTION: call handoff_to_tester with summary including planId={planId} "
            + "so the Tester can verify the role's privilege matrix against the plan.";
    }
}
