using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateGlobalOptionSetTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateGlobalOptionSetTool> logger)
{
    [Description("Execute the creation of a global option set in Dataverse using a pre-built OData JSON payload "
        + "from plan_create_global_option_set. Creates the option set, adds it to the solution, updates project state, "
        + "and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateGlobalOptionSetAsync(
        [Description("The full OData JSON payload from plan_create_global_option_set")] string webApiPayloadJson,
        [Description("The schema name of the global option set")] string optionSetName,
        [Description("The solution unique name to add the option set to")] string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ExecuteCreateGlobalOptionSet executing: optionSetName={OptionSetName}, solution={Solution}",
            optionSetName, solutionUniqueName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var metadataId = await dataverseClient.CreateGlobalOptionSetAsync(webApiPayloadJson, cancellationToken);

            // Add to solution (ComponentType 9 = OptionSet)
            await dataverseClient.AddSolutionComponentAsync(metadataId, 9, solutionUniqueName, cancellationToken);

            state.OptionSets.Add(new CreatedOptionSet(metadataId, optionSetName, IsGlobal: true, DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "ExecuteCreateGlobalOptionSet",
                Parameters = new { optionSetName, solutionUniqueName },
                Result = new { metadataId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteGlobalOptionSet",
                    Parameters = new { metadataId, optionSetName },
                    Warning = "Deleting a global option set removes it from all fields that reference it.",
                    RiskLevel = RiskLevel.High
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreateGlobalOptionSet",
                Detail = $"Created global option set '{optionSetName}' in solution '{solutionUniqueName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateGlobalOptionSet succeeded: metadataId={MetadataId}, optionSetName={OptionSetName}",
                metadataId, optionSetName);

            return $"Global option set '{optionSetName}' created with MetadataId={metadataId} and added to solution '{solutionUniqueName}'.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateGlobalOptionSet failed: optionSetName={OptionSetName}", optionSetName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreateGlobalOptionSet",
                Detail = $"Failed to create global option set '{optionSetName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"ERROR: Failed to create global option set '{optionSetName}': {ex.Message}. Check payload and environment. You may retry or hand off to Solution Architect.";
        }
    }
}
