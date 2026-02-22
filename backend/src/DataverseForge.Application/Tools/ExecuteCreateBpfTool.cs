using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateBpfTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a Business Process Flow using a pre-built OData JSON payload from plan_create_bpf. "
        + "Creates the BPF, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateBpfAsync(
        [Description("The full OData JSON payload from plan_create_bpf")] string webApiPayloadJson,
        [Description("The BPF name")] string bpfName,
        [Description("The primary table logical name")] string primaryTable,
        CancellationToken cancellationToken)
    {
        var processId = await dataverseClient.CreateBpfAsync(webApiPayloadJson, cancellationToken);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.BusinessProcessFlows.Add(new CreatedBpf(processId, bpfName, primaryTable, DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.AutomationBuilder,
            ToolName = "ExecuteCreateBpf",
            Parameters = new { bpfName, primaryTable },
            Result = new { processId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteBpf",
                Parameters = new { processId, bpfName },
                Warning = "Deleting a BPF removes all active instances and stage data.",
                RiskLevel = RiskLevel.High
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"BPF '{bpfName}' created on table '{primaryTable}' with ProcessId={processId}.";
    }
}
