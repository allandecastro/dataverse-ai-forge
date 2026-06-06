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
                Guid? bu = null;
                if (p.TryGetProperty("BusinessUnitId", out var bel)
                    && bel.ValueKind == JsonValueKind.String
                    && Guid.TryParse(bel.GetString(), out var parsedBu))
                {
                    bu = parsedBu;
                }
                privileges.Add(new RolePrivilegeAssignment(id, depth, name, bu));
            }
        }

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
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.SecurityBuilder, "ExecuteCreateSecurityRole", $"Failed to create role '{roleName}': {ex.Message}"));
            }, cancellationToken);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteCreateSecurityRole", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to create role '{roleName}': {ex.Message}";
        }

        // If privilege attach fails after the role row was created, the role exists empty.
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

        var solutionUniqueName = state.SolutionUniqueName;
        var solutionAddNote = string.Empty;
        if (!string.IsNullOrWhiteSpace(solutionUniqueName))
        {
            try
            {
                // Roles reject DoNotIncludeSubcomponents=true with HTTP 400
                // 0x80040216 "DoNotIncludeSubcomponents can not be set to true
                // on non Entity root ... of logicalName 20" (component-type 20
                // = Role). The "alignment to true" commit (7539192) caused
                // every role add-to-solution to fail on real envs from
                // 2026-05-13 onwards. Roles store privileges separately from
                // the solution component graph anyway, so subcomp doesn't
                // change export behavior either way — set false to match what
                // Dataverse accepts.
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
                IsReversible = true,
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
            fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.SecurityBuilder, "ExecuteCreateSecurityRole", $"Created role '{roleName}' (RoleId={newRoleId}) with {privileges.Count} privilege(s)"));
        }, cancellationToken);

        sw.Stop();
        ForgeActivitySource.RecordToolDuration("ExecuteCreateSecurityRole", "success", sw.ElapsedMilliseconds);

        return $"Role '{roleName}' created with RoleId={newRoleId} and {privileges.Count} "
            + $"privilege(s) attached.{solutionAddNote}{privilegeError} "
            + $"NEXT REQUIRED ACTION: call handoff_to_tester with summary including planId={planId} "
            + "so the Tester can verify the role's privilege matrix against the plan.";
    }
}
