using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanUpdateWorkflowTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    [Description("Generate a plan for renaming or re-describing an existing Dataverse workflow "
        + "(Business Process Flow OR Business Rule). The structural definition (BPF stages / "
        + "Business Rule conditions + actions) is NOT touched — v1 only supports name + "
        + "description PATCH. To change a workflow's structure, deactivate, delete, and create "
        + "a new one (Business Rules) OR edit in the maker portal (BPFs). REFUSES on managed "
        + "workflows.")]
    public async Task<WorkflowVerificationResult> PlanUpdateWorkflowAsync(
        [Description("WorkflowId GUID — from get_workflows")] Guid workflowId,
        [Description("Optional new name. Pass empty to leave unchanged.")] string newName,
        [Description("Optional new description. Pass empty to leave unchanged.")] string newDescription,
        CancellationToken cancellationToken)
    {
        if (workflowId == Guid.Empty)
        {
            return Reject(workflowId, "RoleId is required. Call get_workflows first and pass the GUID.");
        }

        var live = await dataverseClient.GetWorkflowAsync(workflowId, cancellationToken);
        if (live is null)
        {
            return Reject(workflowId, $"Workflow {workflowId} not found in this environment.");
        }
        if (live.IsManaged)
        {
            return RejectManaged(workflowId, live.Name);
        }

        var hasRename = !string.IsNullOrWhiteSpace(newName) && !string.Equals(newName, live.Name, StringComparison.Ordinal);
        var hasDescChange = !string.IsNullOrWhiteSpace(newDescription) && !string.Equals(newDescription, live.Description, StringComparison.Ordinal);
        if (!hasRename && !hasDescChange)
        {
            return Reject(workflowId, "No-op: newName and newDescription are either empty or already match the live workflow.");
        }

        var patch = new Dictionary<string, object>();
        if (hasRename) patch["name"] = newName;
        if (hasDescChange) patch["description"] = newDescription;
        var payloadJson = JsonSerializer.Serialize(patch);

        var kindPrefix = live.Category == WorkflowCategory.BusinessProcessFlow
            ? "update_bpf"
            : "update_business_rule";
        var planId = $"{kindPrefix}_{Guid.NewGuid():N}";

        // Wrap the patch so the verifier can pull workflowId + the original
        // category back out of state.ExecutedPlans without re-reading live.
        var composite = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["workflowId"] = workflowId,
            ["name"] = hasRename ? newName : live.Name,
            ["primaryentity"] = live.PrimaryEntityLogicalName,
            ["category"] = (int)live.Category,
            ["patch"] = patch,
        });

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, composite);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new WorkflowVerificationResult(
            WorkflowId: workflowId,
            PlanId: planId,
            Verdict: "yellow",
            Discrepancies: new List<Discrepancy>
            {
                new("workflow", live.Name, $"will be renamed/re-described — staged under {planId}"),
            });
    }

    private static WorkflowVerificationResult Reject(Guid workflowId, string reason) =>
        new(
            WorkflowId: workflowId,
            PlanId: $"rejected_update_workflow_invalid_{Guid.NewGuid():N}",
            Verdict: "red",
            Discrepancies: new List<Discrepancy> { new("reason", "REJECTED", reason) });

    private static WorkflowVerificationResult RejectManaged(Guid workflowId, string name) =>
        new(
            WorkflowId: workflowId,
            PlanId: $"rejected_update_workflow_managed_{Guid.NewGuid():N}",
            Verdict: "red",
            Discrepancies: new List<Discrepancy>
            {
                new("reason", "REJECTED",
                    $"Workflow '{name}' is managed (shipped via a managed solution). Managed "
                    + "workflows can't be edited via the Web API — direct the user to the maker portal."),
            });
}
