using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class UndoLastOperationTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Undo the last executed operation by reversing it in Dataverse. "
        + "Shows the user what will be undone and the risk level before executing. "
        + "Only reversible operations can be undone. High-risk undos require explicit confirmation.")]
    public async Task<string> UndoLastOperationAsync(
        [Description("Set to true to confirm the undo after reviewing the warning")] bool confirmUndo,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (state.OperationStack.Count == 0)
        {
            return "No operations to undo. The operation stack is empty.";
        }

        var operation = state.OperationStack.Peek();

        if (!operation.IsReversible || operation.ReverseOp == null)
        {
            return $"The last operation '{operation.ToolName}' (by {operation.AgentName}) is not reversible.";
        }

        if (!confirmUndo)
        {
            return $"**Undo Preview:**\n"
                + $"- Operation: {operation.ToolName}\n"
                + $"- Agent: {operation.AgentName}\n"
                + $"- Executed at: {operation.ExecutedAt:u}\n"
                + $"- Reverse action: {operation.ReverseOp.ToolName}\n"
                + $"- Risk level: {operation.ReverseOp.RiskLevel}\n"
                + $"- Warning: {operation.ReverseOp.Warning}\n\n"
                + "Call undo_last_operation again with confirmUndo=true to proceed.";
        }

        try
        {
            await ExecuteReverseOperationAsync(operation.ReverseOp, cancellationToken);

            // Pop the operation from the stack
            state.OperationStack.Pop();

            // Remove the artifact from project state
            RemoveArtifactFromState(state, operation);

            // Log the undo in audit
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = "SolutionArchitect",
                Action = $"Undo:{operation.ToolName}",
                Detail = $"Reversed operation '{operation.ToolName}' via {operation.ReverseOp.ToolName}",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"Successfully undone: '{operation.ToolName}' reversed via '{operation.ReverseOp.ToolName}'. "
                + $"Operation stack now has {state.OperationStack.Count} entries.";
        }
        catch (Exception ex)
        {
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = "SolutionArchitect",
                Action = $"UndoFailed:{operation.ToolName}",
                Detail = $"Failed to reverse: {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"Failed to undo '{operation.ToolName}': {ex.Message}. "
                + "The operation remains on the stack. You may need to resolve the issue manually.";
        }
    }

    [Description("List the recent operations on the undo stack, showing what can be reversed.")]
    public async Task<string> ListUndoableOperationsAsync(
        [Description("Number of recent operations to show (default 5)")] int count,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (state.OperationStack.Count == 0)
        {
            return "The operation stack is empty. No operations to undo.";
        }

        var ops = state.OperationStack.Take(count <= 0 ? 5 : count).ToList();
        var lines = new List<string> { $"**Operation Stack** ({state.OperationStack.Count} total):\n" };

        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            var marker = i == 0 ? " (NEXT TO UNDO)" : "";
            var reversible = op.IsReversible ? "reversible" : "NOT reversible";
            lines.Add($"{i + 1}. [{op.ExecutedAt:HH:mm:ss}] {op.AgentName} → {op.ToolName} ({reversible}){marker}");
        }

        return string.Join("\n", lines);
    }

    private async Task ExecuteReverseOperationAsync(ReverseOperation reverseOp, CancellationToken cancellationToken)
    {
        // Parse parameters as JSON for flexible access
        var paramsJson = JsonSerializer.Serialize(reverseOp.Parameters);
        var paramsDoc = JsonDocument.Parse(paramsJson);
        var root = paramsDoc.RootElement;

        switch (reverseOp.ToolName)
        {
            case "DeleteTable":
                var tableMetadataId = root.GetProperty("metadataId").GetGuid();
                await dataverseClient.DeleteTableAsync(tableMetadataId, cancellationToken);
                break;

            case "DeleteField":
                var tableId = root.GetProperty("tableMetadataId").GetGuid();
                var fieldId = root.GetProperty("fieldMetadataId").GetGuid();
                await dataverseClient.DeleteFieldAsync(tableId, fieldId, cancellationToken);
                break;

            case "DeleteRelationship":
                var relId = root.GetProperty("metadataId").GetGuid();
                await dataverseClient.DeleteRelationshipAsync(relId, cancellationToken);
                break;

            case "DeleteForm":
                var formId = root.GetProperty("formId").GetGuid();
                await dataverseClient.DeleteFormAsync(formId, cancellationToken);
                break;

            case "DeleteView":
                var viewId = root.GetProperty("viewId").GetGuid();
                await dataverseClient.DeleteViewAsync(viewId, cancellationToken);
                break;

            case "DeleteDashboard":
                var dashboardId = root.GetProperty("dashboardId").GetGuid();
                await dataverseClient.DeleteDashboardAsync(dashboardId, cancellationToken);
                break;

            case "DeleteSecurityRole":
                var roleId = root.GetProperty("roleId").GetGuid();
                await dataverseClient.DeleteSecurityRoleAsync(roleId, cancellationToken);
                break;

            case "DeleteBpf":
            case "DeleteBusinessRule":
            case "DeleteCustomApi":
                var workflowId = root.GetProperty("workflowId").GetGuid();
                await dataverseClient.DeleteWorkflowAsync(workflowId, cancellationToken);
                break;

            case "DeleteRecords":
                var entitySetName = root.GetProperty("entitySetName").GetString()!;
                var recordIds = root.GetProperty("recordIds").EnumerateArray()
                    .Select(e => e.GetGuid()).ToList();
                await dataverseClient.DeleteRecordsBatchAsync(entitySetName, recordIds, cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"Unknown reverse operation: {reverseOp.ToolName}");
        }
    }

    private static void RemoveArtifactFromState(ProjectState state, ExecutedOperation operation)
    {
        // Parse the result to find the ID of the artifact to remove
        if (operation.Result == null) return;

        var resultJson = JsonSerializer.Serialize(operation.Result);
        var resultDoc = JsonDocument.Parse(resultJson);
        var root = resultDoc.RootElement;

        switch (operation.ToolName)
        {
            case "ExecuteCreateTable":
                if (root.TryGetProperty("metadataId", out var tId))
                {
                    var id = tId.GetGuid();
                    state.Tables.RemoveAll(t => t.MetadataId == id);
                }
                break;

            case "ExecuteCreateField":
                if (root.TryGetProperty("metadataId", out var fId))
                {
                    var id = fId.GetGuid();
                    state.Fields.RemoveAll(f => f.MetadataId == id);
                }
                break;

            case "ExecuteCreateOneToManyRelationship":
            case "ExecuteCreateManyToManyRelationship":
                if (root.TryGetProperty("metadataId", out var rId))
                {
                    var id = rId.GetGuid();
                    state.Relationships.RemoveAll(r => r.MetadataId == id);
                }
                break;

            case "ExecuteCreateForm":
                if (root.TryGetProperty("formId", out var formId))
                {
                    var id = formId.GetGuid();
                    state.Forms.RemoveAll(f => f.FormId == id);
                }
                break;

            case "ExecuteCreateView":
                if (root.TryGetProperty("viewId", out var viewId))
                {
                    var id = viewId.GetGuid();
                    state.Views.RemoveAll(v => v.SavedQueryId == id);
                }
                break;

            case "ExecuteCreateDashboard":
                if (root.TryGetProperty("dashboardId", out var dashId))
                {
                    var id = dashId.GetGuid();
                    state.Dashboards.RemoveAll(d => d.FormId == id);
                }
                break;

            case "ExecuteCreateSecurityRole":
                if (root.TryGetProperty("roleId", out var roleId))
                {
                    var id = roleId.GetGuid();
                    state.SecurityRoles.RemoveAll(r => r.RoleId == id);
                }
                break;

            case "ExecuteCreateBpf":
                if (root.TryGetProperty("workflowId", out var bpfId))
                {
                    var id = bpfId.GetGuid();
                    state.BusinessProcessFlows.RemoveAll(b => b.ProcessId == id);
                }
                break;

            case "ExecutePopulateReferenceData":
            case "ExecuteGenerateTestData":
                // Seed data records are tagged with table name — remove matching recent records
                break;
        }
    }
}
