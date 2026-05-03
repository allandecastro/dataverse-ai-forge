using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Adds an existing Dataverse component to the active solution. Generalizes
/// AddExistingTableToSolutionTool across the kinds we commonly hit: table,
/// field (column), form, view, alternate key, global option set. The tool
/// resolves the component's MetadataId from the supplied identifier(s) so
/// the agent never has to pass a GUID. Relationships are out of scope here
/// (their MetadataId isn't surfaced through the current schema reader);
/// those land in the active solution automatically when created via
/// execute_create_one_to_many_relationship / execute_create_many_to_many_relationship.
/// </summary>
public class AddExistingComponentToSolutionTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<AddExistingComponentToSolutionTool> logger)
{
    [Description("Add an EXISTING Dataverse component (already in the environment) to the ACTIVE solution. "
        + "Use this when the user wants to include something that pre-exists — e.g. an OOB form on Account, "
        + "a custom view they built in the maker portal, a global option set someone else created. "
        + "For brand-new components created in this session, the execute_* tools already auto-add to the "
        + "active solution; you don't need this tool there. "
        + "componentKind values: 'table', 'field', 'form', 'view', 'alt_key', 'option_set'. "
        + "Identifier rules per kind are described on each parameter. "
        + "Relationships aren't supported here — use plan_create_one_to_many_relationship / "
        + "plan_create_many_to_many_relationship to create new ones (they auto-add).")]
    public async Task<string> AddExistingComponentToSolutionAsync(
        [Description("Component kind: 'table', 'field', 'form', 'view', 'alt_key', 'option_set'.")] string componentKind,
        [Description("Primary identifier. Per kind: "
            + "table → table logical name (e.g. 'account'). "
            + "field → field logical name (e.g. 'eppc_giftname'); ALSO pass tableLogicalName. "
            + "form → form GUID (savedformid; from get_forms_list). "
            + "view → view GUID (savedqueryid; from get_views_list). "
            + "alt_key → alt-key logical name (e.g. 'eppc_external_id_key'); ALSO pass tableLogicalName. "
            + "option_set → global option set Name (e.g. 'eppc_giftcategory') OR display name.")] string identifier,
        [Description("Required for componentKind='field' or 'alt_key'. The logical name of the parent table. Empty otherwise.")] string tableLogicalName,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (string.IsNullOrWhiteSpace(state.SolutionUniqueName))
        {
            return "ERROR: No active solution. Call select_solution or create_solution first.";
        }

        var kind = (componentKind ?? string.Empty).Trim().ToLowerInvariant();
        var id = (identifier ?? string.Empty).Trim();
        var t = (tableLogicalName ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(id))
        {
            return "ERROR: identifier is required.";
        }

        try
        {
            Guid componentId;
            SolutionComponentType componentType;
            string label;

            switch (kind)
            {
                case "table":
                {
                    var meta = await dataverseClient.GetTableMetadataAsync(id.ToLowerInvariant(), cancellationToken);
                    if (meta.MetadataId == Guid.Empty)
                        return $"ERROR: Table '{id}' has no MetadataId.";
                    componentId = meta.MetadataId;
                    componentType = SolutionComponentType.Entity;
                    label = $"table '{meta.DisplayName}' ({id.ToLowerInvariant()})";
                    break;
                }
                case "field":
                case "column":
                case "attribute":
                {
                    if (string.IsNullOrWhiteSpace(t))
                        return "ERROR: tableLogicalName is required when componentKind='field'.";
                    var meta = await dataverseClient.GetFieldMetadataAsync(t, id.ToLowerInvariant(), cancellationToken);
                    if (meta.MetadataId == Guid.Empty)
                        return $"ERROR: Field '{id}' on '{t}' has no MetadataId.";
                    componentId = meta.MetadataId;
                    componentType = SolutionComponentType.Attribute;
                    label = $"field '{meta.DisplayName}' ({t}.{id.ToLowerInvariant()})";
                    break;
                }
                case "form":
                {
                    // Form/view IDs are passed verbatim — there is no logical-name
                    // lookup for them. Validate it parses as a Guid before sending
                    // to Dataverse so we get a clean rejection rather than a 400.
                    if (!Guid.TryParse(id, out var formId) || formId == Guid.Empty)
                        return "ERROR: For componentKind='form', identifier must be a valid GUID (savedformid). Use get_forms_list to find it.";
                    componentId = formId;
                    componentType = SolutionComponentType.Form;
                    label = $"form (formId={formId})";
                    break;
                }
                case "view":
                case "savedquery":
                {
                    if (!Guid.TryParse(id, out var viewId) || viewId == Guid.Empty)
                        return "ERROR: For componentKind='view', identifier must be a valid GUID (savedqueryid). Use get_views_list to find it.";
                    componentId = viewId;
                    componentType = SolutionComponentType.SavedQuery;
                    label = $"view (savedQueryId={viewId})";
                    break;
                }
                case "alt_key":
                case "alternatekey":
                case "entity_key":
                {
                    if (string.IsNullOrWhiteSpace(t))
                        return "ERROR: tableLogicalName is required when componentKind='alt_key'.";
                    var keys = await dataverseClient.GetAlternateKeysAsync(t, cancellationToken);
                    var match = keys.FirstOrDefault(k =>
                        string.Equals(k.LogicalName, id, StringComparison.OrdinalIgnoreCase));
                    if (match is null || match.MetadataId == Guid.Empty)
                        return $"ERROR: Alternate key '{id}' not found on table '{t}'. Available: " +
                            string.Join(", ", keys.Select(k => k.LogicalName));
                    componentId = match.MetadataId;
                    componentType = SolutionComponentType.EntityKey;
                    label = $"alt key '{match.DisplayName}' ({t}.{match.LogicalName})";
                    break;
                }
                case "option_set":
                case "optionset":
                case "global_option_set":
                {
                    var resolved = await dataverseClient.ResolveGlobalOptionSetMetadataIdAsync(id, cancellationToken);
                    if (resolved is null || resolved == Guid.Empty)
                        return $"ERROR: Global option set '{id}' not found. Use get_global_option_sets to find the exact Name.";
                    componentId = resolved.Value;
                    componentType = SolutionComponentType.OptionSet;
                    label = $"global option set '{id}'";
                    break;
                }
                default:
                    return $"ERROR: Unsupported componentKind='{componentKind}'. "
                        + "Use one of: table, field, form, view, alt_key, option_set. "
                        + "Relationships are auto-added on creation; there's no add-existing path here.";
            }

            await dataverseClient.AddSolutionComponentAsync(
                componentId,
                componentType,
                state.SolutionUniqueName,
                doNotIncludeSubcomponents: true,
                cancellationToken);

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.SolutionArchitect,
                ToolName = "AddExistingComponentToSolution",
                Parameters = new { componentKind = kind, identifier = id, tableLogicalName = t, solutionUniqueName = state.SolutionUniqueName },
                Result = new { componentId, componentType = (int)componentType },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "RemoveSolutionComponent",
                    Parameters = new
                    {
                        componentId,
                        componentType = (int)componentType,
                        solutionUniqueName = state.SolutionUniqueName
                    },
                    Warning = "Removes the component from the solution. The component itself remains in the environment.",
                    RiskLevel = RiskLevel.Low
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SolutionArchitect,
                Action = "AddExistingComponentToSolution",
                Detail = $"Added {label} to solution '{state.SolutionUniqueName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("Added existing {Kind} {Id} to solution {Solution}", kind, componentId, state.SolutionUniqueName);

            return $"Added {label} to solution '{state.SolutionDisplayName ?? state.SolutionUniqueName}'.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AddExistingComponentToSolution failed: kind={Kind}, id={Id}, table={Table}", kind, id, t);
            return $"ERROR: Could not add {kind} '{id}' to solution '{state.SolutionUniqueName}': {ex.Message}";
        }
    }
}
