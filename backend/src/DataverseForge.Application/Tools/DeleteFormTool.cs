using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Direct delete of a Dataverse system form. Mirrors DeleteViewTool /
/// DeleteChartTool — no plan/execute split because there's no payload to
/// mangle, just a formId. The agent MUST request_approval first; deletion
/// is irreversible without a pre-delete FormXML snapshot.
/// </summary>
public class DeleteFormTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DeleteFormTool> logger)
{
    [Description("Delete a Dataverse system form by formId. DESTRUCTIVE: any user assigned to this form "
        + "will fall back to the default. Call request_approval BEFORE this tool. Use get_forms_list to "
        + "find the formId. There is no plan_delete_form — call this directly after approval. The "
        + "default Main form on a table cannot be deleted; Dataverse will reject with HTTP 403.")]
    public async Task<string> DeleteFormAsync(
        [Description("The GUID of the form to delete (formid from get_forms_list)")] string formId,
        [Description("The logical name of the table the form belongs to (for audit logging)")] string tableLogicalName,
        [Description("The display name of the form (for audit logging)")] string formName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("DeleteForm executing: formId={FormId}, table={Table}, name={Name}",
            formId, tableLogicalName, formName);

        if (!Guid.TryParse(formId, out var parsedFormId) || parsedFormId == Guid.Empty)
        {
            return "ERROR: formId must be a valid GUID — call get_forms_list to retrieve it.";
        }

        try
        {
            await dataverseClient.DeleteFormAsync(parsedFormId, cancellationToken);

            // F-23: re-read state right before mutate+save.
            // Reverse op: same caveat as DeleteView — not trivially undoable
            // without a pre-delete FormXML snapshot. A future enhancement could
            // call GetFormDefinitionAsync first and store the spec for replay.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.Forms.RemoveAll(f => f.FormId == parsedFormId);
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    ToolName = "DeleteForm",
                    Parameters = new { formId, tableLogicalName, formName },
                    Result = new { formId },
                    IsReversible = false,
                    ReverseOp = null
                });
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "DeleteForm",
                    Detail = $"Deleted form '{formName}' (formId={parsedFormId}) from table '{tableLogicalName}'",
                    Success = true
                });
            }, cancellationToken);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "DeleteForm"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Form '{formName}' (formId={parsedFormId}) deleted from table '{tableLogicalName}'. "
                + "Users assigned to this form will fall back to the default form.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "DeleteForm failed: formId={FormId}, name={Name}", parsedFormId, formName);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "DeleteForm",
                    Detail = $"Failed to delete form '{formName}' (formId={parsedFormId}) on table '{tableLogicalName}': {ex.Message}",
                    Success = false
                });
            }, cancellationToken);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "DeleteForm"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to delete form '{formName}' (formId={parsedFormId}): {ex.Message}. "
                + "Common causes: it's the table's default Main form (Dataverse blocks deletion), "
                + "or the form is part of a managed solution.";
        }
    }
}
