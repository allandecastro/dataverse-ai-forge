using DataverseForge.Application.Agents;
using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteModifyFieldTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteModifyFieldTool> logger)
{
    [Description("Execute the modification of an existing field. Pass the planId from "
        + "plan_modify_field — the OData JSON payload was stored server-side and will be "
        + "loaded automatically. Do NOT try to construct the payload yourself. The target "
        + "table and field are identified by their LOGICAL names — never pass MetadataId GUIDs.")]
    public async Task<string> ExecuteModifyFieldAsync(
        [Description("planId returned by plan_modify_field — must match exactly")] string planId,
        [Description("Logical name of the table (e.g. 'eppc_gift'). The MetadataId is resolved server-side.")] string tableLogicalName,
        [Description("Logical name of the field (e.g. 'eppc_giftname'). The MetadataId is resolved server-side.")] string fieldLogicalName,
        [Description("Summary of what changed (e.g., 'displayName, requiredLevel')")] string changeSummary,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteModifyField executing: planId={PlanId}, table={TableLogicalName}, field={FieldLogicalName}",
            planId, tableLogicalName, fieldLogicalName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        // Standard 3-arm planId resolution — Pending / AlreadyExecuted / NotFound.
        // Without this, an agent that re-fires Execute on the same planId after a
        // verifier loop sees "not found" and tries to re-plan, doubling the work.
        // CLAUDE.md routing/idempotency: every Execute_* should use LookupPlan.
        var planLookup = state.LookupPlan(planId, out var webApiPayloadJson);
        if (planLookup == PlanLookupStatus.AlreadyExecuted)
        {
            return $"ALREADY_EXECUTED: planId '{planId}' was already executed earlier in this session. The field modification has shipped — do NOT retry. Hand off to the Tester to verify.";
        }
        if (planLookup == PlanLookupStatus.NotFound)
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_modify_field first and pass its planId verbatim.";
        }

        if (string.IsNullOrWhiteSpace(tableLogicalName) || string.IsNullOrWhiteSpace(fieldLogicalName))
        {
            return "ERROR: tableLogicalName and fieldLogicalName are required.";
        }

        // Snapshot the field's current metadata BEFORE mutating so we can store
        // a reversible undo. Best-effort: a read failure here doesn't block the
        // modify (the user already approved the destructive intent), we just
        // emit a non-reversible op-stack entry instead.
        FieldMetadata? preModifySnapshot = null;
        try
        {
            preModifySnapshot = await dataverseClient.GetFieldMetadataAsync(tableLogicalName, fieldLogicalName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ExecuteModifyField pre-modify snapshot failed — proceeding without reversible undo");
        }

        try
        {
            await dataverseClient.ModifyFieldAsync(tableLogicalName, fieldLogicalName, webApiPayloadJson, cancellationToken);

            // Reverse-op snapshots the field metadata fields the modify tool can
            // flip back. Falls back to non-reversible if the pre-modify read
            // failed. Only the fields FieldMetadata exposes are captured —
            // Type-shape changes (e.g. text→memo) cannot be undone in any case.
            var reverseOp = preModifySnapshot is null
                ? null
                : new ReverseOperation
                {
                    ToolName = "ExecuteModifyField",
                    Parameters = new
                    {
                        tableLogicalName,
                        fieldLogicalName,
                        priorDisplayName = preModifySnapshot.DisplayName,
                        priorRequiredLevel = preModifySnapshot.RequiredLevel,
                        priorMaxLength = preModifySnapshot.MaxLength,
                        priorIsSearchable = preModifySnapshot.IsSearchable,
                        priorIsAuditEnabled = preModifySnapshot.IsAuditEnabled,
                    },
                    Warning = "Reverts the field's display name / required level / max length / searchable / audit flags to their pre-modify values. Type-shape changes cannot be undone.",
                    RiskLevel = RiskLevel.Low,
                };

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    ToolName = "ExecuteModifyField",
                    Parameters = new { tableLogicalName, fieldLogicalName, changeSummary },
                    Result = new { fieldLogicalName, changeSummary },
                    // F-25: undo support shipped — UndoLastOperationTool
                    // rebuilds a PATCH from the prior* snapshot fields and
                    // calls ModifyFieldAsync to flip the field back. Only
                    // reversible when the snapshot was successfully captured
                    // (preModifySnapshot is not null → reverseOp is not null).
                    IsReversible = reverseOp is not null,
                    ReverseOp = reverseOp,
                });
                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.DataModelBuilder, "ExecuteModifyField", $"Modified field '{fieldLogicalName}' on table '{tableLogicalName}'. Changes: {changeSummary}"));
            }, cancellationToken);

            logger.LogInformation("ExecuteModifyField succeeded: fieldLogicalName={FieldLogicalName} on {TableLogicalName}", fieldLogicalName, tableLogicalName);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteModifyField", "success", sw.ElapsedMilliseconds);
            return $"Field '{fieldLogicalName.ToLowerInvariant()}' on table '{tableLogicalName.ToLowerInvariant()}' modified successfully. Changes: {changeSummary}. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester so the Tester can verify the field changes.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExecuteModifyField failed: tableLogicalName={TableLogicalName}, fieldLogicalName={FieldLogicalName}", tableLogicalName, fieldLogicalName);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.DataModelBuilder, "ExecuteModifyField", $"Failed to modify field '{fieldLogicalName}' on table '{tableLogicalName}': {ex.Message}"));
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteModifyField", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to modify field '{fieldLogicalName}' on table '{tableLogicalName}': {ex.Message}";
        }
    }
}
