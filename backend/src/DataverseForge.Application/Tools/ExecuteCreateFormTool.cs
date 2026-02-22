using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateFormTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a Dataverse form using a pre-built OData JSON payload from plan_create_form. "
        + "Creates the form, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateFormAsync(
        [Description("The full OData JSON payload from plan_create_form")] string webApiPayloadJson,
        [Description("The logical name of the table the form belongs to")] string tableLogicalName,
        [Description("The display name of the form")] string formName,
        [Description("The form type: 'main', 'quickCreate', or 'quickView'")] string formType,
        CancellationToken cancellationToken)
    {
        var formId = await dataverseClient.CreateFormAsync(webApiPayloadJson, cancellationToken);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.Forms.Add(new CreatedForm(formId, tableLogicalName, formName, formType, DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.UIBuilder,
            ToolName = "ExecuteCreateForm",
            Parameters = new { tableLogicalName, formName, formType },
            Result = new { formId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteForm",
                Parameters = new { formId, tableLogicalName },
                Warning = "Deleting a form removes its layout. Users assigned to this form will fall back to the default.",
                RiskLevel = RiskLevel.Medium
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"Form '{formName}' ({formType}) created on table '{tableLogicalName}' with FormId={formId}.";
    }
}
