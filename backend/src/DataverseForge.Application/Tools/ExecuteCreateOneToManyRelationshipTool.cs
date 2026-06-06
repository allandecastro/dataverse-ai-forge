using DataverseForge.Application.Agents;
using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateOneToManyRelationshipTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateOneToManyRelationshipTool> logger)
{
    [Description("Execute the creation of a 1:N relationship between two Dataverse tables. Pass the planId "
        + "from plan_create_one_to_many_relationship — the OData JSON payload was stored server-side and "
        + "will be loaded automatically. Do NOT try to construct the payload yourself.")]
    public async Task<string> ExecuteCreateOneToManyRelationshipAsync(
        [Description("planId returned by plan_create_one_to_many_relationship — must match exactly")] string planId,
        [Description("The schema name of the relationship")] string relationshipSchemaName,
        [Description("The logical name of the primary (one-side) table")] string primaryTable,
        [Description("The logical name of the related (many-side) table")] string relatedTable,
        [Description("The solution unique name to add the relationship to")] string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateOneToManyRelationship executing: planId={PlanId}, relationshipSchemaName={RelationshipSchemaName}, primaryTable={PrimaryTable}, relatedTable={RelatedTable}",
            planId, relationshipSchemaName, primaryTable, relatedTable);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteCreateOneToManyRelationship: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"Relationship '{relationshipSchemaName}' between '{primaryTable}' and '{relatedTable}' is already in Dataverse. "
                + "DO NOT retry, DO NOT re-plan. Move on to the NEXT pending plan in this turn.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteCreateOneToManyRelationship: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_one_to_many_relationship first and pass its returned planId verbatim.";
        }

        try
        {
            var metadataId = await dataverseClient.CreateOneToManyRelationshipAsync(webApiPayloadJson, cancellationToken);
            await dataverseClient.AddSolutionComponentAsync(metadataId, SolutionComponentType.EntityRelationship, solutionUniqueName, cancellationToken: cancellationToken);

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.Relationships.Add(new CreatedRelationship(metadataId, relationshipSchemaName, primaryTable, relatedTable, "OneToMany", DateTime.UtcNow));
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.DataModelBuilder,
                    ToolName = "ExecuteCreateOneToManyRelationship",
                    Parameters = new { relationshipSchemaName, primaryTable, relatedTable, solutionUniqueName },
                    Result = new { metadataId },
                    IsReversible = true,
                    ReverseOp = new ReverseOperation
                    {
                        ToolName = "DeleteRelationship",
                        Parameters = new { metadataId, relationshipSchemaName },
                        Warning = "Deleting a relationship removes the lookup field and all relationship data.",
                        RiskLevel = RiskLevel.High
                    }
                });
                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.DataModelBuilder, "ExecuteCreateOneToManyRelationship", $"Created 1:N relationship '{relationshipSchemaName}' ({primaryTable} -> {relatedTable}) in solution '{solutionUniqueName}'"));
            }, cancellationToken);

            logger.LogInformation("ExecuteCreateOneToManyRelationship succeeded: metadataId={MetadataId}, relationshipSchemaName={RelationshipSchemaName}",
                metadataId, relationshipSchemaName);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteCreateOneToManyRelationship", "success", sw.ElapsedMilliseconds);
            return $"1:N relationship '{relationshipSchemaName}' ({primaryTable.ToLowerInvariant()} -> {relatedTable.ToLowerInvariant()}) created with MetadataId={metadataId} and added to solution '{solutionUniqueName}'. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester so the Tester can verify the relationship.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateOneToManyRelationship failed: relationshipSchemaName={RelationshipSchemaName}", relationshipSchemaName);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.DataModelBuilder, "ExecuteCreateOneToManyRelationship", $"Failed to create 1:N relationship '{relationshipSchemaName}': {ex.Message}"));
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteCreateOneToManyRelationship", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to create 1:N relationship '{relationshipSchemaName}': {ex.Message}. Check payload and environment. You may retry or hand off to Solution Architect.";
        }
    }
}
