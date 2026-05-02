using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Adds an EXISTING Dataverse table (OOB or already-created custom) to the active
/// solution. Used when the user says "add Account to my solution" — distinct from
/// CreateTable which creates a brand-new table. Wraps Dataverse's AddSolutionComponent
/// action with componentType=1 (Entity).
/// </summary>
public class AddExistingTableToSolutionTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<AddExistingTableToSolutionTool> logger)
{
    [Description("Add an EXISTING Dataverse table (OOB like Account/Contact, OR a custom table that "
        + "already exists in the environment) to the ACTIVE solution. Use this when the user wants to "
        + "include a pre-existing table in their solution — NOT for creating a new table (use plan_create_table "
        + "for that). The active solution is read from project state. The tool resolves the table's MetadataId "
        + "via a live EntityDefinitions lookup, so the agent only needs to pass the logical name.")]
    public async Task<string> AddExistingTableToSolutionAsync(
        [Description("Logical name of the existing table (e.g. 'account', 'contact', 'adc_phone')")] string tableLogicalName,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (string.IsNullOrWhiteSpace(state.SolutionUniqueName))
        {
            return "ERROR: No active solution. Call select_solution or create_solution first.";
        }

        var lowered = tableLogicalName.ToLowerInvariant();
        try
        {
            var meta = await dataverseClient.GetTableMetadataAsync(lowered, cancellationToken);
            if (meta.MetadataId == Guid.Empty)
            {
                return $"ERROR: Table '{lowered}' was found but has no MetadataId. Aborting.";
            }

            // Add ONLY the table itself, not its subcomponents (fields, forms, views,
            // relationships). Past confusion: when adding an existing table like Contact,
            // Dataverse's default behavior pulls in dozens of OOB columns/forms/views the
            // user didn't ask for. doNotIncludeSubcomponents=true keeps the addition tight;
            // a separate confirmation can decide whether to pull in specific subcomponents.
            // The Web API action is idempotent for already-included components.
            await dataverseClient.AddSolutionComponentAsync(meta.MetadataId, SolutionComponentType.Entity, state.SolutionUniqueName, doNotIncludeSubcomponents: true, cancellationToken);

            logger.LogInformation("Added existing table {Table} ({MetadataId}) to solution {Solution}",
                lowered, meta.MetadataId, state.SolutionUniqueName);

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.SolutionArchitect,
                ToolName = "AddExistingTableToSolution",
                Parameters = new { tableLogicalName = lowered, solutionUniqueName = state.SolutionUniqueName },
                Result = new { metadataId = meta.MetadataId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "RemoveSolutionComponent",
                    Parameters = new
                    {
                        componentId = meta.MetadataId,
                        componentType = (int)SolutionComponentType.Entity,
                        solutionUniqueName = state.SolutionUniqueName
                    },
                    Warning = "Removes the table from the solution. The table itself remains in the environment with its data intact.",
                    RiskLevel = RiskLevel.Low
                }
            });
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SolutionArchitect,
                Action = "AddExistingTableToSolution",
                Detail = $"Added '{lowered}' to solution '{state.SolutionUniqueName}'",
                Success = true
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"Added the '{meta.DisplayName}' table ({lowered}) to solution '{state.SolutionDisplayName}' — "
                + "TABLE ONLY (no fields, forms, views, or relationships). If the user also wants specific "
                + "subcomponents (e.g. the main form, a particular view, a custom field), ASK first which "
                + "ones, then add each as a separate step. Do NOT add all subcomponents by default.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AddExistingTableToSolution failed for {Table}", lowered);
            return $"ERROR: Could not add '{lowered}' to solution '{state.SolutionUniqueName}': {ex.Message}";
        }
    }
}
