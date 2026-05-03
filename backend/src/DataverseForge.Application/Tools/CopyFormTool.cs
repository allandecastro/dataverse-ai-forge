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
/// Clones an existing systemform via the bound CopySystemForm action. The new
/// form inherits FormXML, type, objecttypecode, and ancestor from the source;
/// only <c>name</c> and (optional) <c>description</c> are overridden. Useful
/// when the user asks for "a new form just like X but called Y" — much faster
/// than rebuilding from plan_create_form.
/// </summary>
public class CopyFormTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<CopyFormTool> logger)
{
    [Description("Clone an existing Dataverse systemform via the bound CopySystemForm action. "
        + "Use this when the user wants 'a copy of <existing form>', 'a variant of the main form', "
        + "or 'duplicate this form'. The new form inherits FormXML/type/objecttypecode from the source — "
        + "you only specify the new display name and optional description. The new form lands in the "
        + "active solution automatically (via CopySystemForm's server-side semantics). "
        + "Call request_approval BEFORE this tool — copies are persistent artifacts. "
        + "After success, call handoff_to_tester so the Tester can verify the copy.")]
    public async Task<string> CopyFormAsync(
        [Description("GUID of the source form to copy (savedformid; from get_forms_list)")] string sourceFormId,
        [Description("Display name for the new (copied) form (e.g., 'Gift Quick View Variant')")] string newFormName,
        [Description("Optional description for the new form. Empty string for none.")] string newDescription,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        if (!Guid.TryParse(sourceFormId, out var parsedSourceId) || parsedSourceId == Guid.Empty)
        {
            return $"ERROR: sourceFormId '{sourceFormId}' is not a valid GUID. Use get_forms_list to find the correct savedformid.";
        }
        if (string.IsNullOrWhiteSpace(newFormName))
        {
            return "ERROR: newFormName is required.";
        }

        logger.LogInformation("CopyForm executing: sourceFormId={Source}, newName={Name}", parsedSourceId, newFormName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var newFormId = await dataverseClient.CopyFormAsync(parsedSourceId, newFormName, newDescription ?? string.Empty, cancellationToken);

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "CopyForm",
                Parameters = new { sourceFormId = parsedSourceId, newFormName, newDescription },
                Result = new { newFormId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteForm",
                    Parameters = new { formId = newFormId },
                    Warning = "Deletes the copied form. The source form is unaffected.",
                    RiskLevel = RiskLevel.Medium
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "CopyForm",
                Detail = $"Copied form {parsedSourceId} → '{newFormName}' (FormId={newFormId})",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "CopyForm"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Form copied: source={parsedSourceId} → new FormId={newFormId} (name='{newFormName}'). "
                + "The new form inherits the source's FormXML, type, and target table. "
                + "NEXT: call handoff_to_tester so the Tester can verify the copy.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "CopyForm failed: source={Source}", parsedSourceId);
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "CopyForm",
                Detail = $"Failed to copy form {parsedSourceId}: {ex.Message}",
                Success = false
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "CopyForm"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to copy form {parsedSourceId}: {ex.Message}. "
                + "Common causes: the source formId doesn't exist, the form is from a managed solution that "
                + "blocks copying, or the new name conflicts with an existing form on the same table.";
        }
    }
}
