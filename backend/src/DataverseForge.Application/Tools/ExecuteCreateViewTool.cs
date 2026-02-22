using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateViewTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a Dataverse system view using a pre-built OData JSON payload from plan_create_view. "
        + "Creates the view, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateViewAsync(
        [Description("The full OData JSON payload from plan_create_view")] string webApiPayloadJson,
        [Description("The logical name of the table the view belongs to")] string tableLogicalName,
        [Description("The display name of the view")] string viewName,
        CancellationToken cancellationToken)
    {
        var savedQueryId = await dataverseClient.CreateViewAsync(webApiPayloadJson, cancellationToken);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.Views.Add(new CreatedView(savedQueryId, tableLogicalName, viewName, DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.UIBuilder,
            ToolName = "ExecuteCreateView",
            Parameters = new { tableLogicalName, viewName },
            Result = new { savedQueryId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteView",
                Parameters = new { savedQueryId, tableLogicalName },
                Warning = "Deleting a view removes it from all model-driven apps that reference it.",
                RiskLevel = RiskLevel.Medium
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"View '{viewName}' created on table '{tableLogicalName}' with SavedQueryId={savedQueryId}.";
    }
}
