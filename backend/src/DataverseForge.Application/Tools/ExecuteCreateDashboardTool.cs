using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateDashboardTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a Dataverse dashboard using a pre-built OData JSON payload from plan_create_dashboard. "
        + "Creates the dashboard, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateDashboardAsync(
        [Description("The full OData JSON payload from plan_create_dashboard")] string webApiPayloadJson,
        [Description("The display name of the dashboard")] string dashboardName,
        CancellationToken cancellationToken)
    {
        var formId = await dataverseClient.CreateDashboardAsync(webApiPayloadJson, cancellationToken);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.Dashboards.Add(new CreatedDashboard(formId, dashboardName, DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.UIBuilder,
            ToolName = "ExecuteCreateDashboard",
            Parameters = new { dashboardName },
            Result = new { formId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteDashboard",
                Parameters = new { formId },
                Warning = "Deleting a dashboard removes it from all users who have it pinned.",
                RiskLevel = RiskLevel.Medium
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"Dashboard '{dashboardName}' created with FormId={formId}.";
    }
}
