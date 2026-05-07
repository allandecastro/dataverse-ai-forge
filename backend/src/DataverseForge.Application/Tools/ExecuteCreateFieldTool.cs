using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateFieldTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateFieldTool> logger)
{
    [Description("Execute the creation of a field on a Dataverse table. Pass the planId from "
        + "plan_create_field — the OData JSON payload was stored server-side and will be loaded "
        + "automatically. Do NOT try to construct the payload yourself. The target table is "
        + "resolved from tableLogicalName — never pass a MetadataId GUID.")]
    public async Task<string> ExecuteCreateFieldAsync(
        [Description("planId returned by plan_create_field — must match exactly")] string planId,
        [Description("Logical name of the target table (e.g., 'adc_vehicle'). The MetadataId is "
            + "looked up server-side — DO NOT pass GUIDs.")] string tableLogicalName,
        [Description("Logical name of the field being created (from plan_create_field.fieldLogicalName)")] string fieldLogicalName,
        [Description("Display name of the field")] string displayName,
        [Description("Field type (e.g., 'string', 'integer', 'boolean')")] string fieldType,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateField executing: planId={PlanId}, tableLogicalName={TableLogicalName}, fieldLogicalName={FieldLogicalName}, fieldType={FieldType}",
            planId, tableLogicalName, fieldLogicalName, fieldType);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        // Reject plans that plan_create_field flagged as duplicate-existing — the
        // agent should never have batched these. Returning a clear error keeps the
        // model from looping on a Dataverse 400.
        if (planId.StartsWith("rejected_field_exists_", StringComparison.Ordinal))
        {
            logger.LogInformation("ExecuteCreateField: skipping rejected (already-exists) plan: {PlanId}", planId);
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_field because the field "
                + $"already exists on table '{tableLogicalName}'. Treat the requirement as satisfied "
                + "and DO NOT retry. Use plan_modify_field if a property change is needed.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteCreateField: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier in this session. "
                + $"Field '{fieldLogicalName}' is already in Dataverse on table '{tableLogicalName}'. "
                + "DO NOT retry, DO NOT re-plan. Move on to the NEXT pending plan in this turn — "
                + "if the user just approved a different plan (e.g. a Customer/Polymorphic lookup), "
                + "execute THAT planId, not this one.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteCreateField: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_field first and pass its returned planId verbatim.";
        }

        // Resolve tableMetadataId from tableLogicalName ONLY. We never accept a
        // MetadataId GUID from the agent — those are routinely hallucinated and
        // sending an invented GUID to Dataverse produces silent 404s. Resolution:
        //   1. Look up by logical name in project state (tables created this session).
        //   2. Live EntityDefinitions lookup against Dataverse.
        //   3. Otherwise return an error (operation is genuinely unrecoverable).
        var loweredLogical = tableLogicalName.ToLowerInvariant();
        var solutionUniqueName = state.SolutionUniqueName; // capture before await
        Guid resolvedTableId;
        // Capture an entry to add to fresh state's Tables cache iff the live
        // lookup happens below — applied inside the F-23 ApplyAndSaveAsync so
        // it survives any concurrent state mutation.
        CreatedTable? cachedTableEntry = null;

        var match = state.Tables.FirstOrDefault(t =>
            string.Equals(t.LogicalName, loweredLogical, StringComparison.OrdinalIgnoreCase));
        if (match is not null && match.MetadataId != Guid.Empty)
        {
            resolvedTableId = match.MetadataId;
            logger.LogInformation("Resolved tableMetadataId={MetadataId} for {Logical} from project state", resolvedTableId, loweredLogical);
        }
        else
        {
            try
            {
                var live = await dataverseClient.GetTableMetadataAsync(loweredLogical, cancellationToken);
                resolvedTableId = live.MetadataId;
                cachedTableEntry = new CreatedTable(live.MetadataId, loweredLogical, live.DisplayName, DateTime.UtcNow);
                logger.LogInformation("Resolved tableMetadataId={MetadataId} for {Logical} via live lookup", live.MetadataId, loweredLogical);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not resolve tableMetadataId for {Logical} — table not in project state and live lookup failed", loweredLogical);
                return $"ERROR: Could not resolve table '{tableLogicalName}'. "
                    + $"It is not in project state and a live Dataverse lookup failed: {ex.Message}. "
                    + "Verify the table logical name is correct and that the table exists in the current environment.";
            }
        }

        try
        {
            var metadataId = await dataverseClient.CreateFieldAsync(resolvedTableId, webApiPayloadJson, cancellationToken);

            // Add the new column to the active solution. Without this the field
            // exists in the environment but the user's solution doesn't track it,
            // so it won't ship when they export. Best-effort — if the add fails,
            // the field still exists in the env, log a warning but don't fail.
            var solutionAddNote = string.Empty;
            if (!string.IsNullOrWhiteSpace(solutionUniqueName))
            {
                try
                {
                    await dataverseClient.AddSolutionComponentAsync(metadataId, SolutionComponentType.Attribute, solutionUniqueName, doNotIncludeSubcomponents: true, cancellationToken);
                    solutionAddNote = $" Added to solution '{solutionUniqueName}'.";
                }
                catch (Exception solEx)
                {
                    logger.LogWarning(solEx, "AddSolutionComponent failed for field {FieldLogicalName} ({MetadataId}) on solution {Solution}; column exists in env but not tracked by solution",
                        fieldLogicalName, metadataId, solutionUniqueName);
                    solutionAddNote = $" WARNING: column created in env but could NOT be added to solution '{solutionUniqueName}': {solEx.Message}.";
                }
            }
            else
            {
                solutionAddNote = " WARNING: no active solution selected — the field is in the default solution. Use select_solution and add_existing_component_to_solution if it should ship in a specific solution.";
            }

            // F-23: re-read state immediately before mutating + saving so we
            // don't clobber concurrent writes that landed during the ~5–30s of
            // Dataverse work above. The plan lookup happened on the initial
            // snapshot (correct — planId is tied to that snapshot), but the
            // additive mutations below all need to land on fresh state.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                if (cachedTableEntry is not null
                    && !fresh.Tables.Any(t => string.Equals(t.LogicalName, cachedTableEntry.LogicalName, StringComparison.OrdinalIgnoreCase)))
                {
                    fresh.Tables.Add(cachedTableEntry);
                }
                fresh.Fields.Add(new CreatedField(metadataId, tableLogicalName.ToLowerInvariant(), fieldLogicalName.ToLowerInvariant(), displayName, fieldType, DateTime.UtcNow));
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    ToolName = "ExecuteCreateField",
                    Parameters = new { tableLogicalName, fieldLogicalName, displayName, fieldType },
                    Result = new { metadataId },
                    IsReversible = true,
                    ReverseOp = new ReverseOperation
                    {
                        ToolName = "DeleteField",
                        Parameters = new { tableMetadataId = resolvedTableId, fieldMetadataId = metadataId, fieldLogicalName },
                        Warning = "Deleting a field removes all data stored in it.",
                        RiskLevel = RiskLevel.High
                    }
                });
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    Action = "ExecuteCreateField",
                    Detail = $"Created field '{displayName}' ({fieldLogicalName}) on table '{tableLogicalName}'",
                    Success = true
                });
            }, cancellationToken);

            logger.LogInformation("ExecuteCreateField succeeded: metadataId={MetadataId}, fieldLogicalName={FieldLogicalName}, tableLogicalName={TableLogicalName}",
                metadataId, fieldLogicalName, tableLogicalName);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateField"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Field '{displayName}' ({fieldLogicalName.ToLowerInvariant()}) of type '{fieldType}' created on table '{tableLogicalName.ToLowerInvariant()}' with MetadataId={metadataId}.{solutionAddNote} "
                + $"NEXT REQUIRED ACTION: call handoff_to_tester with summary including planId={planId} so the Tester can verify against the plan.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExecuteCreateField failed: fieldLogicalName={FieldLogicalName}, tableLogicalName={TableLogicalName}", fieldLogicalName, tableLogicalName);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    Action = "ExecuteCreateField",
                    Detail = $"Failed to create field '{fieldLogicalName}' on '{tableLogicalName}': {ex.Message}",
                    Success = false
                });
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateField"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create field '{displayName}' ({fieldLogicalName}): {ex.Message}";
        }
    }
}
