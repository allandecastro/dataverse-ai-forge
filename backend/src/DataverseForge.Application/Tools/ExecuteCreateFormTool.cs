using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateFormTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateFormTool> logger)
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
        logger.LogInformation("ExecuteCreateForm executing: tableLogicalName={TableLogicalName}, formName={FormName}, formType={FormType}",
            tableLogicalName, formName, formType);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var formId = await dataverseClient.CreateFormAsync(webApiPayloadJson, cancellationToken);

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

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateForm",
                Detail = $"Created form '{formName}' ({formType}) on table '{tableLogicalName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateForm succeeded: formId={FormId}, formName={FormName}", formId, formName);
            return $"Form '{formName}' ({formType}) created on table '{tableLogicalName}' with FormId={formId}.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateForm failed: tableLogicalName={TableLogicalName}, formName={FormName}", tableLogicalName, formName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateForm",
                Detail = $"Failed to create form '{formName}' on table '{tableLogicalName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"ERROR: Failed to create form '{formName}' ({formType}) on table '{tableLogicalName}': {ex.Message}. "
                + "Check that the table exists and the payload is valid. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
