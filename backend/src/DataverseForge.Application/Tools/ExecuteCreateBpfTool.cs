using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateBpfTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateBpfTool> logger)
{
    [Description("Execute the creation of a Business Process Flow using a pre-built OData JSON payload from plan_create_bpf. "
        + "Creates the BPF, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateBpfAsync(
        [Description("The full OData JSON payload from plan_create_bpf")] string webApiPayloadJson,
        [Description("The BPF name")] string bpfName,
        [Description("The primary table logical name")] string primaryTable,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateBpf executing: bpfName={BpfName}, primaryTable={PrimaryTable}", bpfName, primaryTable);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var processId = await dataverseClient.CreateBpfAsync(webApiPayloadJson, cancellationToken);

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

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                Action = "ExecuteCreateBpf",
                Detail = $"Created BPF '{bpfName}' on table '{primaryTable}' with ProcessId={processId}",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateBpf succeeded: processId={ProcessId}", processId);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateBpf"), new KeyValuePair<string, object?>("tool.status", "success"));

            return $"BPF '{bpfName}' created on table '{primaryTable}' with ProcessId={processId}.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateBpf failed: bpfName={BpfName}, primaryTable={PrimaryTable}", bpfName, primaryTable);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                Action = "ExecuteCreateBpf",
                Detail = $"Failed to create BPF '{bpfName}' on table '{primaryTable}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateBpf"), new KeyValuePair<string, object?>("tool.status", "error"));

            return $"ERROR: Failed to create BPF '{bpfName}' on table '{primaryTable}'. {ex.Message}. Check connectivity and payload format.";
        }
    }
}
