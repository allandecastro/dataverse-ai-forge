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

            case "DeleteRecords":
                var entitySetName = root.GetProperty("entitySetName").GetString()!;
                var recordIds = root.GetProperty("recordIds").EnumerateArray()
                    .Select(e => e.GetGuid()).ToList();
                await dataverseClient.DeleteRecordsBatchAsync(entitySetName, recordIds, cancellationToken);
                break;

            case "RemoveSolutionComponent":
                var rscComponentId = root.GetProperty("componentId").GetGuid();
                var rscComponentType = (SolutionComponentType)root.GetProperty("componentType").GetInt32();
                var rscSolutionName = root.GetProperty("solutionUniqueName").GetString()!;
                await dataverseClient.RemoveSolutionComponentAsync(
                    rscComponentId, rscComponentType, rscSolutionName, cancellationToken);
                break;

            case "DeleteSolution":
                var solutionId = root.GetProperty("solutionId").GetGuid();
                await dataverseClient.DeleteSolutionAsync(solutionId, cancellationToken);
                break;

            case "DeletePublisher":
                var publisherId = root.GetProperty("publisherId").GetGuid();
                await dataverseClient.DeletePublisherAsync(publisherId, cancellationToken);
                break;

            case "RevertSolutionVersion":
                // Restoring the previous version is itself an UpdateSolutionVersion call —
                // the saved Parameters carry the old version string we're rolling back to.
                var rsvSolutionId = root.GetProperty("solutionId").GetGuid();
                var rsvOldVersion = root.GetProperty("oldVersion").GetString()!;
                await dataverseClient.UpdateSolutionVersionAsync(rsvSolutionId, rsvOldVersion, cancellationToken);
                break;

            case "DeleteGlobalOptionSet":
                // F-25: undo for ExecuteCreateGlobalOptionSet. Cascades references
                // on Choice columns server-side.
                var gosMetadataId = root.GetProperty("metadataId").GetGuid();
                await dataverseClient.DeleteGlobalOptionSetAsync(gosMetadataId, cancellationToken);
                break;

            case "ExecuteModifyField":
                // F-25: undo for ExecuteModifyField. The reverse-op stored a
                // pre-modify snapshot; rebuild a PATCH that flips the fields
                // back. Only the metadata FieldMetadata captures is undoable —
                // type-shape changes can't be reversed (and we don't try).
                var emfTable = root.GetProperty("tableLogicalName").GetString()!;
                var emfField = root.GetProperty("fieldLogicalName").GetString()!;
                var emfPayload = BuildModifyFieldUndoPayload(root);
                await dataverseClient.ModifyFieldAsync(emfTable, emfField, emfPayload, cancellationToken);
                break;

            case "DeleteSecurityRole":
                // Phase 6 / Commit 3 — undo for ExecuteCreateSecurityRole.
                // Wired here so the role record gets deleted; the recreate path
                // (running execute_create_security_role again) is the user's
                // job — the reverse-op only tears down the artifact.
                // CAVEAT: this restores nothing about the privilege matrix.
                // A redo via plan_create gets a NEW RoleId; existing user
                // assignments DON'T move across (Dataverse's delete cascades
                // those, so undo doesn't need to clean them up).
                var dsrRoleId = root.GetProperty("roleId").GetGuid();
                await dataverseClient.DeleteRoleAsync(dsrRoleId, cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"Unknown reverse operation: {reverseOp.ToolName}");
        }
    }

    /// <summary>
    /// Rebuilds a PATCH payload for ModifyFieldAsync from the prior* fields
    /// stashed in the reverse-op Parameters. Only emits keys that were in the
    /// snapshot — partial-snapshot scenarios still produce a valid PATCH.
    /// Source: Microsoft Learn — "Update column definitions using the Web API".
    /// </summary>
    private static string BuildModifyFieldUndoPayload(JsonElement root)
    {
        var payload = new Dictionary<string, object>
        {
            // Generic AttributeMetadata @odata.type — Dataverse rebinds to the
            // concrete subtype on the server side based on the attribute's
            // existing kind. Using the base type means we don't need to know
            // whether the field is String / Picklist / etc. to undo a flip.
            ["@odata.type"] = "Microsoft.Dynamics.CRM.AttributeMetadata",
        };
        if (root.TryGetProperty("priorDisplayName", out var dn) && dn.ValueKind == JsonValueKind.String)
        {
            payload["DisplayName"] = ToolHelpers.BuildLocalizedLabel(dn.GetString() ?? string.Empty);
        }
        if (root.TryGetProperty("priorRequiredLevel", out var rl) && rl.ValueKind == JsonValueKind.String)
        {
            payload["RequiredLevel"] = new Dictionary<string, object>
            {
                ["Value"] = rl.GetString() ?? "None",
                ["CanBeChanged"] = true,
            };
        }
        if (root.TryGetProperty("priorMaxLength", out var ml) && ml.ValueKind == JsonValueKind.Number && ml.TryGetInt32(out var mlValue))
        {
            payload["MaxLength"] = mlValue;
        }
        if (root.TryGetProperty("priorIsSearchable", out var srch) && (srch.ValueKind == JsonValueKind.True || srch.ValueKind == JsonValueKind.False))
        {
            payload["IsValidForAdvancedFind"] = new Dictionary<string, object>
            {
                ["Value"] = srch.GetBoolean(),
                ["CanBeChanged"] = true,
            };
        }
        if (root.TryGetProperty("priorIsAuditEnabled", out var aud) && (aud.ValueKind == JsonValueKind.True || aud.ValueKind == JsonValueKind.False))
        {
            payload["IsAuditEnabled"] = new Dictionary<string, object>
            {
                ["Value"] = aud.GetBoolean(),
                ["CanBeChanged"] = true,
            };
        }
        return JsonSerializer.Serialize(payload);
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

            case "ExecutePopulateReferenceData":
            case "ExecuteGenerateTestData":
                // Seed data records are tagged with table name — remove matching recent records
                break;
        }
    }
}
