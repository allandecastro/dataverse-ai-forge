using DataverseForge.Application.Agents;
using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateManyToManyRelationshipTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateManyToManyRelationshipTool> logger)
{
    [Description("Execute the creation of an N:N (many-to-many) relationship between two Dataverse tables. "
        + "Pass the planId from plan_create_many_to_many_relationship — the OData JSON payload was stored "
        + "server-side and will be loaded automatically. Do NOT try to construct the payload yourself.")]
    public async Task<string> ExecuteCreateManyToManyRelationshipAsync(
        [Description("planId returned by plan_create_many_to_many_relationship — must match exactly")] string planId,
        [Description("The schema name of the relationship")] string relationshipSchemaName,
        [Description("The logical name of the first table")] string firstTable,
        [Description("The logical name of the second table")] string secondTable,
        [Description("The name of the intersect table")] string intersectTableName,
        [Description("The solution unique name to add the relationship to")] string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateManyToManyRelationship executing: planId={PlanId}, relationshipSchemaName={RelationshipSchemaName}, firstTable={FirstTable}, secondTable={SecondTable}",
            planId, relationshipSchemaName, firstTable, secondTable);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteCreateManyToManyRelationship: planId {PlanId} already executed", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + "The N:N relationship is already in Dataverse. DO NOT retry, DO NOT re-plan. "
                + "Move on to the NEXT pending plan in this turn.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteCreateManyToManyRelationship: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_many_to_many_relationship first and pass its returned planId verbatim.";
        }

        try
        {
            var metadataId = await dataverseClient.CreateManyToManyRelationshipAsync(webApiPayloadJson, cancellationToken);
            await dataverseClient.AddSolutionComponentAsync(metadataId, SolutionComponentType.EntityRelationship, solutionUniqueName, cancellationToken: cancellationToken);

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.Relationships.Add(new CreatedRelationship(metadataId, relationshipSchemaName, firstTable, secondTable, "ManyToMany", DateTime.UtcNow));
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    ToolName = "ExecuteCreateManyToManyRelationship",
                    Parameters = new { relationshipSchemaName, firstTable, secondTable, intersectTableName, solutionUniqueName },
                    Result = new { metadataId },
                    IsReversible = true,
                    ReverseOp = new ReverseOperation
                    {
                        ToolName = "DeleteRelationship",
                        Parameters = new { metadataId, relationshipSchemaName },
                        Warning = "Deleting an N:N relationship removes the intersect table and all association data.",
                        RiskLevel = RiskLevel.High
                    }
                });
                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.DataModelBuilder, "ExecuteCreateManyToManyRelationship", $"Created N:N relationship '{relationshipSchemaName}' ({firstTable} <-> {secondTable}) in solution '{solutionUniqueName}'"));
            }, cancellationToken);

            logger.LogInformation("ExecuteCreateManyToManyRelationship succeeded: metadataId={MetadataId}, relationshipSchemaName={RelationshipSchemaName}",
                metadataId, relationshipSchemaName);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteCreateManyToManyRelationship", "success", sw.ElapsedMilliseconds);
            return $"N:N relationship '{relationshipSchemaName}' ({firstTable.ToLowerInvariant()} <-> {secondTable.ToLowerInvariant()}) created with MetadataId={metadataId}, intersect table '{intersectTableName.ToLowerInvariant()}', and added to solution '{solutionUniqueName}'. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester so the Tester can verify the relationship.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateManyToManyRelationship failed: relationshipSchemaName={RelationshipSchemaName}", relationshipSchemaName);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.DataModelBuilder, "ExecuteCreateManyToManyRelationship", $"Failed to create N:N relationship '{relationshipSchemaName}': {ex.Message}"));
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteCreateManyToManyRelationship", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to create N:N relationship '{relationshipSchemaName}': {ex.Message}. Check payload and environment. You may retry or hand off to Solution Architect.";
        }
    }
}
