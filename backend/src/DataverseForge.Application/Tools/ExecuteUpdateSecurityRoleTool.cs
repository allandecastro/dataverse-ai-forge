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

public class ExecuteUpdateSecurityRoleTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteUpdateSecurityRoleTool> logger)
{
    [Description("Execute a security-role update — runs the rename PATCH (if any), then the "
        + "privilege add list (single AddPrivilegesRole call), then the privilege remove list "
        + "(looped — Dataverse RemovePrivilegeRole takes one privilege per call). Pass the "
        + "planId from plan_update_security_role. The delta payload was stored server-side "
        + "and is loaded automatically. IsReversible=false in v1 — the privilege state before "
        + "the edit isn't snapshotted, so undo doesn't restore the prior matrix.")]
    public async Task<string> ExecuteUpdateSecurityRoleAsync(
        [Description("planId returned by plan_update_security_role — must match exactly")] string planId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteUpdateSecurityRole executing: planId={PlanId}", planId);

        if (planId.StartsWith("rejected_update_role_", StringComparison.Ordinal))
        {
            logger.LogInformation("ExecuteUpdateSecurityRole: skipping rejected plan: {PlanId}", planId);
            return $"SKIPPED: Plan '{planId}' was rejected by plan_update_security_role. "
                + "Re-run plan_update_security_role with corrected inputs.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var lookupStatus = state.LookupPlan(planId, out var compositePayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteUpdateSecurityRole: planId {PlanId} already executed", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier in this session. "
                + "DO NOT retry. If the user wants another change, call plan_update_security_role "
                + "with the new target spec.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteUpdateSecurityRole: planId not found: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_update_security_role first.";
        }

        var doc = JsonDocument.Parse(compositePayloadJson);
        var roleId = doc.RootElement.GetProperty("roleId").GetGuid();
        var addList = ReadPrivilegeList(doc.RootElement, "add");
        var removeList = ReadPrivilegeList(doc.RootElement, "remove");
        var rolePatch = doc.RootElement.GetProperty("rolePatch");
        var hasRename = rolePatch.EnumerateObject().Any();

        var report = new List<string>();
        var failures = new List<string>();

        if (hasRename)
        {
            try
            {
                var patchJson = rolePatch.GetRawText();
                await dataverseClient.UpdateRoleAsync(roleId, patchJson, cancellationToken);
                report.Add("Role metadata patched (name/description).");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ExecuteUpdateSecurityRole: UpdateRole PATCH failed for role {RoleId}", roleId);
                failures.Add($"PATCH failed: {ex.Message}");
            }
        }

        if (addList.Count > 0)
        {
            try
            {
                await dataverseClient.AddPrivilegesToRoleAsync(roleId, addList, cancellationToken);
                report.Add($"Added/updated {addList.Count} privilege(s).");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ExecuteUpdateSecurityRole: AddPrivilegesRole failed for role {RoleId}", roleId);
                failures.Add($"AddPrivileges failed: {ex.Message}");
            }
        }

        // Dataverse's RemovePrivilegeRole takes one privilege per call.
        var removed = 0;
        foreach (var p in removeList)
        {
            try
            {
                await dataverseClient.RemovePrivilegeFromRoleAsync(roleId, p.PrivilegeId, cancellationToken);
                removed++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ExecuteUpdateSecurityRole: RemovePrivilegeRole failed for privilege {PrivilegeId}", p.PrivilegeId);
                failures.Add($"Remove failed for privilege {p.DebugName ?? p.PrivilegeId.ToString()}: {ex.Message}");
            }
        }
        if (removeList.Count > 0)
        {
            report.Add($"Removed {removed}/{removeList.Count} privilege(s).");
        }

        var succeeded = failures.Count == 0;

        await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
        {
            fresh.ExecutedPlans[planId] = compositePayloadJson;
            fresh.PendingPlans.Remove(planId);
            fresh.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.SecurityBuilder,
                ToolName = "ExecuteUpdateSecurityRole",
                Parameters = new { roleId, addCount = addList.Count, removeCount = removeList.Count, planId },
                Result = new { roleId, planId, succeeded },
                IsReversible = false,
                ReverseOp = null,
            });
            fresh.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SecurityBuilder,
                Action = "ExecuteUpdateSecurityRole",
                Detail = succeeded
                    ? string.Join("; ", report)
                    : $"PARTIAL: {string.Join("; ", report)} | failures: {string.Join("; ", failures)}",
                Success = succeeded,
            });
        }, cancellationToken);

        sw.Stop();
        ForgeActivitySource.RecordToolDuration("ExecuteUpdateSecurityRole", succeeded ? "success" : "partial", sw.ElapsedMilliseconds);

        var summary = string.Join(" ", report);
        if (failures.Count == 0)
        {
            return $"Role {roleId} updated successfully. {summary} "
                + $"NEXT REQUIRED ACTION: call handoff_to_tester with planId={planId} so the "
                + "Tester can verify the post-delta state.";
        }

        // Partial failure — surface the failed steps verbatim so the agent can
        // explain to the user what stuck and what didn't.
        return $"PARTIAL: Role {roleId} update finished with failures. {summary} "
            + "Failures: " + string.Join(" | ", failures) + ". "
            + $"NEXT REQUIRED ACTION: surface the failure list to the user; offer to re-plan "
            + $"with planId={planId} or call list_pending_plans to inspect state.";
    }

    private static List<RolePrivilegeAssignment> ReadPrivilegeList(JsonElement root, string property)
    {
        var list = new List<RolePrivilegeAssignment>();
        if (!root.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var el in arr.EnumerateArray())
        {
            var id = el.GetProperty("PrivilegeId").GetGuid();
            var depth = el.GetProperty("Depth").GetString() ?? "Basic";
            var name = el.TryGetProperty("Name", out var n) ? n.GetString() : null;
            Guid? bu = null;
            if (el.TryGetProperty("BusinessUnitId", out var bel)
                && bel.ValueKind == JsonValueKind.String
                && Guid.TryParse(bel.GetString(), out var parsedBu))
            {
                bu = parsedBu;
            }
            list.Add(new RolePrivilegeAssignment(id, depth, name, bu));
        }
        return list;
    }
}
