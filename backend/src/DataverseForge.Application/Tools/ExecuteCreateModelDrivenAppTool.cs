using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateModelDrivenAppTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a Dataverse model-driven app using a pre-built OData JSON payload from plan_create_model_driven_app. "
        + "Creates the app, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateModelDrivenAppAsync(
        [Description("The full OData JSON payload from plan_create_model_driven_app")] string webApiPayloadJson,
        [Description("The display name of the app")] string appName,
        [Description("The unique name of the app")] string appUniqueName,
        CancellationToken cancellationToken)
    {
        var appId = await dataverseClient.CreateModelDrivenAppAsync(webApiPayloadJson, cancellationToken);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.ModelDrivenApps.Add(new CreatedModelDrivenApp(appId, appUniqueName, appName, DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.UIBuilder,
            ToolName = "ExecuteCreateModelDrivenApp",
            Parameters = new { appName, appUniqueName },
            Result = new { appId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteModelDrivenApp",
                Parameters = new { appId, appUniqueName },
                Warning = "Deleting a model-driven app removes it from the app launcher for all users.",
                RiskLevel = RiskLevel.High
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"Model-driven app '{appName}' ({appUniqueName}) created with AppId={appId}.";
    }
}
