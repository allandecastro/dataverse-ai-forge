using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateCustomApiTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a Dataverse Custom API using a pre-built OData JSON payload from plan_create_custom_api. "
        + "Creates the Custom API, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateCustomApiAsync(
        [Description("The full OData JSON payload from plan_create_custom_api")] string webApiPayloadJson,
        [Description("The unique name of the Custom API")] string uniqueName,
        [Description("The display name of the Custom API")] string displayName,
        CancellationToken cancellationToken)
    {
        var customApiId = await dataverseClient.CreateCustomApiAsync(webApiPayloadJson, cancellationToken);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        // Custom APIs are tracked as plugins in project state (they're backed by plugin types)
        state.Plugins.Add(new CreatedPlugin(customApiId, displayName, string.Empty, "CustomApi", 0, DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.AutomationBuilder,
            ToolName = "ExecuteCreateCustomApi",
            Parameters = new { uniqueName, displayName },
            Result = new { customApiId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteCustomApi",
                Parameters = new { customApiId, uniqueName },
                Warning = "Deleting a Custom API removes the endpoint and breaks any integrations using it.",
                RiskLevel = RiskLevel.High
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"Custom API '{displayName}' ({uniqueName}) created with CustomApiId={customApiId}.";
    }
}
