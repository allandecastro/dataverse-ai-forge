using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tester-side verifier for AutomationBuilder workflow plans. Routes by
/// planId prefix:
///   • <c>create_bpf_*</c> / <c>update_bpf_*</c> → diff BPF metadata
///   • <c>create_business_rule_*</c> / <c>update_business_rule_*</c>
///     → diff Business Rule metadata
///
/// v1 verifies metadata only (name, description, primaryentity, category,
/// activation state) — full XAML diffing is deferred because the
/// XAML schema is fragile and Dataverse normalizes / pretty-prints
/// what it stores. Confirming the workflow exists with the right shape
/// is sufficient for v1; the user does the deep "did the flow render
/// correctly" check in the maker portal.
/// </summary>
public class VerifyWorkflowAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a Business Process Flow or Business Rule against the planned spec. "
        + "Loads the planned payload from server-side state (under the planId returned by "
        + "plan_create_business_process_flow / plan_create_business_rule / plan_update_workflow) "
        + "and compares against the live workflow record fetched by workflowId. Returns "
        + "PASS / FAIL with concrete discrepancies. PlanId must start with 'create_bpf_', "
        + "'update_bpf_', 'create_business_rule_', or 'update_business_rule_'. ALWAYS use "
        + "this after execute_create_business_process_flow or execute_create_business_rule.")]
    public async Task<WorkflowVerificationResult> VerifyWorkflowAgainstPlanAsync(
        [Description("planId from plan_create_business_process_flow / plan_create_business_rule")]
        string planId,
        [Description("workflowId GUID — from execute_create_business_process_flow / execute_create_business_rule result")]
        string workflowId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(workflowId, out var parsedWorkflowId) || parsedWorkflowId == Guid.Empty)
        {
            return new WorkflowVerificationResult(
                Guid.Empty, planId, "FAIL",
                [new Discrepancy("workflowId", workflowId, "not a valid GUID")]);
        }

        var isBpf = planId.StartsWith("create_bpf_", StringComparison.Ordinal)
                    || planId.StartsWith("update_bpf_", StringComparison.Ordinal);
        var isBusinessRule = planId.StartsWith("create_business_rule_", StringComparison.Ordinal)
                             || planId.StartsWith("update_business_rule_", StringComparison.Ordinal);
        if (!isBpf && !isBusinessRule)
        {
            if (planId.StartsWith("rejected_bpf_", StringComparison.Ordinal) ||
                planId.StartsWith("rejected_business_rule_", StringComparison.Ordinal))
            {
                return new WorkflowVerificationResult(
                    parsedWorkflowId, planId, "FAIL",
                    [new Discrepancy("plan", planId,
                        "REJECTED PLAN: was rejected at planning time and never reached "
                        + "Dataverse — re-plan with valid inputs (do NOT pass this planId).")]);
            }
            return new WorkflowVerificationResult(
                parsedWorkflowId, planId, "FAIL",
                [new Discrepancy("planId", planId,
                    "WRONG PLAN TYPE — verify_workflow_against_plan only accepts "
                    + "create_bpf_*, update_bpf_*, create_business_rule_*, or "
                    + "update_business_rule_* planIds.")]);
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!state.ExecutedPlans.TryGetValue(planId, out var plannedPayload))
        {
            var pendingHint = state.PendingPlans.ContainsKey(planId)
                ? " The plan is still pending — execute_* hasn't been called yet."
                : string.Empty;
            return new WorkflowVerificationResult(
                parsedWorkflowId, planId, "FAIL",
                [new Discrepancy("plan", planId, $"NOT FOUND in ExecutedPlans.{pendingHint}")]);
        }

        WorkflowInfo? live;
        try
        {
            live = await dataverseClient.GetWorkflowAsync(parsedWorkflowId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WorkflowVerificationResult(
                parsedWorkflowId, planId, "FAIL",
                [new Discrepancy("workflow", parsedWorkflowId.ToString(), $"read failed: {ex.Message}")]);
        }
        if (live is null)
        {
            return new WorkflowVerificationResult(
                parsedWorkflowId, planId, "FAIL",
                [new Discrepancy("workflow", parsedWorkflowId.ToString(), "NOT FOUND in environment")]);
        }

        var plan = JsonDocument.Parse(plannedPayload).RootElement;
        var discrepancies = new List<Discrepancy>();

        if (plan.TryGetProperty("name", out var nameEl))
        {
            var expectedName = nameEl.GetString() ?? string.Empty;
            if (!string.Equals(expectedName, live.Name, StringComparison.OrdinalIgnoreCase))
            {
                discrepancies.Add(new Discrepancy("name", expectedName, live.Name));
            }
        }

        if (plan.TryGetProperty("primaryentity", out var entEl))
        {
            var expectedEntity = entEl.GetString() ?? string.Empty;
            if (!string.Equals(expectedEntity, live.PrimaryEntityLogicalName, StringComparison.OrdinalIgnoreCase))
            {
                discrepancies.Add(new Discrepancy("primaryentity", expectedEntity, live.PrimaryEntityLogicalName));
            }
        }

        if (plan.TryGetProperty("category", out var catEl) && catEl.ValueKind == JsonValueKind.Number)
        {
            var expectedCategory = catEl.GetInt32();
            var liveCategory = (int)live.Category;
            if (expectedCategory != liveCategory)
            {
                discrepancies.Add(new Discrepancy("category", expectedCategory.ToString(), liveCategory.ToString()));
            }
        }

        // Activation state isn't part of the create payload (workflows always
        // POST in draft, statecode=0) — the verifier doesn't check it. Activate
        // is a separate user-visible step.

        return new WorkflowVerificationResult(
            parsedWorkflowId,
            planId,
            discrepancies.Count == 0 ? "PASS" : "FAIL",
            discrepancies);
    }
}
