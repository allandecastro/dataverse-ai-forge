using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetWorkflowsTool(IDataverseClient dataverseClient)
{
    [Description("List Dataverse workflows by category. By default (categoryFilter='all'), returns "
        + "Business Process Flows + Business Rules — the two categories the Automation Builder "
        + "manages. Use categoryFilter='bpf' or 'business-rule' to narrow. Filter by table with "
        + "primaryEntityLogicalName (lowercase). Pre-create check: confirm a duplicate doesn't "
        + "exist before planning a new workflow.")]
    public async Task<IReadOnlyList<WorkflowInfo>> GetWorkflowsAsync(
        [Description("Filter by category: 'all' (default) → BPF+BR; 'bpf' → BPFs only; "
            + "'business-rule' → Business Rules only")] string categoryFilter = "all",
        [Description("Optional: filter to workflows for a specific table (lowercase logical name)")]
        string primaryEntityLogicalName = "",
        CancellationToken cancellationToken = default)
    {
        WorkflowCategory? category = categoryFilter?.Trim().ToLowerInvariant() switch
        {
            "bpf" or "businessprocessflow" or "business-process-flow" => WorkflowCategory.BusinessProcessFlow,
            "businessrule" or "business-rule" or "br" => WorkflowCategory.BusinessRule,
            _ => null, // null = "all" — the client defaults to BPF+BR when category is null
        };
        var entity = string.IsNullOrWhiteSpace(primaryEntityLogicalName) ? null : primaryEntityLogicalName;
        return await dataverseClient.GetWorkflowsAsync(category, entity, cancellationToken);
    }
}
