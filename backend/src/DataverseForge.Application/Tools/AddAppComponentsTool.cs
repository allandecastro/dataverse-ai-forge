using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Adds components (tables, forms, views, charts, dashboards) to a model-
/// driven app. Direct (no plan/execute split) — the platform's
/// AddAppComponents action is idempotent and atomic per call.
///
/// Special handling for tables: callers pass the entity LOGICAL NAME
/// ("account", "eppc_gift"), not a GUID. The tool resolves the entity's
/// MetadataId before calling the bound action — appmodulecomponent.objectid
/// for an entity component is the METADATA id, not any row id.
/// </summary>
public class AddAppComponentsTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<AddAppComponentsTool> logger)
{
    [Description("Add components (tables / forms / views / charts / dashboards) to a model-driven app. "
        + "Pass the appmoduleid plus a JSON array of {type, id} objects. "
        + "type values: 'table' (id = entity LOGICAL NAME like 'account'), 'form' (id = formid GUID), "
        + "'view' (id = savedqueryid GUID), 'chart' (id = savedqueryvisualizationid GUID), "
        + "'dashboard' (id = formid GUID; dashboards live in systemform). "
        + "For 'table', the tool resolves the entity's MetadataId automatically — DO NOT pass a "
        + "row's primary-key GUID for tables. For everything else, pass the row GUID. "
        + "Direct (no approval needed) but DESTRUCTIVE COMPONENT REMOVAL goes through remove_app_components. "
        + "Returns per-component outcomes so you can report which succeeded/failed.")]
    public async Task<IReadOnlyList<AppComponentOutcome>> AddAppComponentsAsync(
        [Description("appmoduleid (GUID) of the target model-driven app.")] string appId,
        [Description("JSON array of {\"type\":\"<table|form|view|chart|dashboard|sitemap>\", \"id\":\"<logical-name-or-guid>\"} objects.")] string componentsJson,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<AppComponentOutcome>();

        if (!Guid.TryParse(appId, out var parsedAppId) || parsedAppId == Guid.Empty)
        {
            outcomes.Add(new AppComponentOutcome("(unknown)", appId, false, "appId is not a valid GUID."));
            return outcomes.AsReadOnly();
        }

        IReadOnlyList<AppComponentInput>? inputs;
        try
        {
            inputs = JsonSerializer.Deserialize<IReadOnlyList<AppComponentInput>>(
                componentsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException jx)
        {
            outcomes.Add(new AppComponentOutcome("(parse)", componentsJson, false,
                $"componentsJson is not valid JSON: {jx.Message}"));
            return outcomes.AsReadOnly();
        }

        if (inputs is null || inputs.Count == 0)
        {
            outcomes.Add(new AppComponentOutcome("(empty)", "", false, "componentsJson contained no components."));
            return outcomes.AsReadOnly();
        }

        // Resolve table logical names → MetadataIds before assembling the
        // request. Cache lookups to avoid re-hitting EntityDefinitions for
        // duplicate table names within the same call.
        var entityIdCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<AppComponentRef>(inputs.Count);
        var resolvedInputs = new List<AppComponentInput>(inputs.Count);

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Type) || string.IsNullOrWhiteSpace(input.Id))
            {
                outcomes.Add(new AppComponentOutcome(input.Type ?? "", input.Id ?? "", false, "Both type and id are required."));
                continue;
            }

            var typeLower = input.Type.Trim().ToLowerInvariant();

            if (typeLower is "table" or "entity")
            {
                var logicalName = input.Id.Trim().ToLowerInvariant();
                if (!entityIdCache.TryGetValue(logicalName, out var metadataId))
                {
                    try
                    {
                        metadataId = await dataverseClient.GetEntityMetadataIdAsync(logicalName, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        outcomes.Add(new AppComponentOutcome(input.Type, input.Id, false,
                            $"Could not resolve entity '{logicalName}': {ex.Message}"));
                        continue;
                    }
                    entityIdCache[logicalName] = metadataId;
                }

                if (metadataId is null || metadataId.Value == Guid.Empty)
                {
                    outcomes.Add(new AppComponentOutcome(input.Type, input.Id, false,
                        $"Entity '{logicalName}' does not exist in this environment."));
                    continue;
                }

                resolved.Add(new AppComponentRef("entity", metadataId.Value));
                resolvedInputs.Add(input);
            }
            else if (Guid.TryParse(input.Id, out var rowGuid) && rowGuid != Guid.Empty)
            {
                // Validate the friendly kind name maps to something we know how
                // to ship to AddAppComponents. The client's MapComponentRefToOData
                // throws ArgumentException on unknown kinds — catch here so
                // we report per-component instead of bombing the whole batch.
                resolved.Add(new AppComponentRef(typeLower, rowGuid));
                resolvedInputs.Add(input);
            }
            else
            {
                outcomes.Add(new AppComponentOutcome(input.Type, input.Id, false,
                    $"For type '{input.Type}', id must be a valid GUID. (Tables use logical names; everything else uses row GUIDs.)"));
            }
        }

        if (resolved.Count == 0)
        {
            return outcomes.AsReadOnly();
        }

        IReadOnlyList<AppComponentRefOutcome> refOutcomes;
        try
        {
            refOutcomes = await dataverseClient.AddAppComponentsAsync(parsedAppId, resolved, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The client is supposed to convert per-ref failures to outcomes,
            // not throw. A throw here means an unexpected systemic failure
            // (e.g. a malformed argument it didn't validate). Surface as
            // every-ref-failed so the agent gets a clear signal.
            logger.LogError(ex, "AddAppComponents threw unexpectedly: appId={AppId}", parsedAppId);
            for (var i = 0; i < resolvedInputs.Count; i++)
            {
                outcomes.Add(new AppComponentOutcome(resolvedInputs[i].Type, resolvedInputs[i].Id, false, ex.Message));
            }
            return outcomes.AsReadOnly();
        }

        // Map per-ref outcomes back to the agent-facing AppComponentOutcome
        // shape (which carries the original input's Type + Id strings —
        // including pre-resolution table logical names — instead of the
        // post-resolution metadata GUID the client returned). The client
        // result preserves order, so index alignment is sound.
        var anySucceeded = false;
        for (var i = 0; i < resolvedInputs.Count; i++)
        {
            var input = resolvedInputs[i];
            var refOutcome = i < refOutcomes.Count ? refOutcomes[i] : null;
            var succeeded = refOutcome?.Succeeded ?? false;
            if (succeeded) anySucceeded = true;
            outcomes.Add(new AppComponentOutcome(input.Type, input.Id, succeeded, refOutcome?.Error));
        }

        // Only push to OperationStack / AuditLog when at least one component
        // actually shipped. A wholly-failed batch leaves nothing to undo and
        // surfacing "I added 0 components" in the audit log is misleading.
        if (anySucceeded)
        {
            var succeededRefs = refOutcomes.Where(o => o.Succeeded).Select(o => o.Component).ToList();
            var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "AddAppComponents",
                Parameters = new { appId = parsedAppId, count = succeededRefs.Count },
                Result = new { added = succeededRefs.Count, requested = resolved.Count },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "RemoveAppComponents",
                    Parameters = new { appId = parsedAppId, components = succeededRefs },
                    Warning = "Detaches the components from the app. Components themselves stay in the environment.",
                    RiskLevel = RiskLevel.Low,
                },
            });
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "AddAppComponents",
                Detail = $"Added {succeededRefs.Count}/{resolved.Count} components to app {parsedAppId}",
                Success = succeededRefs.Count == resolved.Count,
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        }

        return outcomes.AsReadOnly();
    }
}
