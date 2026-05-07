using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Detaches components from a model-driven app. The components themselves
/// (the table, form, view, …) stay in the environment — only the app/component
/// association is removed. Same input shape as <see cref="AddAppComponentsTool"/>.
/// </summary>
public class RemoveAppComponentsTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<RemoveAppComponentsTool> logger)
{
    [Description("Remove components from a model-driven app. DESTRUCTIVE on the app's navigation — "
        + "request_approval BEFORE calling. The components themselves stay in the environment; only "
        + "the app↔component association is removed. Same input shape as add_app_components: "
        + "type values are 'table' (id = logical name), 'form' / 'view' / 'chart' / 'dashboard' "
        + "/ 'sitemap' (id = GUID). Returns per-component outcomes.")]
    public async Task<IReadOnlyList<AppComponentOutcome>> RemoveAppComponentsAsync(
        [Description("appmoduleid (GUID) of the target model-driven app.")] string appId,
        [Description("JSON array of {type, id} objects — same shape as add_app_components.")] string componentsJson,
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

        // Same metadataid resolution flow as Add — for tables we lookup by
        // logical name; everything else expects a GUID.
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
                resolved.Add(new AppComponentRef(typeLower, rowGuid));
                resolvedInputs.Add(input);
            }
            else
            {
                outcomes.Add(new AppComponentOutcome(input.Type, input.Id, false,
                    $"For type '{input.Type}', id must be a valid GUID."));
            }
        }

        if (resolved.Count == 0)
        {
            return outcomes.AsReadOnly();
        }

        IReadOnlyList<AppComponentRefOutcome> refOutcomes;
        try
        {
            refOutcomes = await dataverseClient.RemoveAppComponentsAsync(parsedAppId, resolved, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "RemoveAppComponents threw unexpectedly: appId={AppId}", parsedAppId);
            foreach (var input in resolvedInputs)
            {
                outcomes.Add(new AppComponentOutcome(input.Type, input.Id, false, ex.Message));
            }
            return outcomes.AsReadOnly();
        }

        var anySucceeded = false;
        for (var i = 0; i < resolvedInputs.Count; i++)
        {
            var input = resolvedInputs[i];
            var refOutcome = i < refOutcomes.Count ? refOutcomes[i] : null;
            var succeeded = refOutcome?.Succeeded ?? false;
            if (succeeded) anySucceeded = true;
            outcomes.Add(new AppComponentOutcome(input.Type, input.Id, succeeded, refOutcome?.Error));
        }

        if (anySucceeded)
        {
            var succeededCount = refOutcomes.Count(o => o.Succeeded);
            var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "RemoveAppComponents",
                Parameters = new { appId = parsedAppId, count = succeededCount },
                Result = new { removed = succeededCount, requested = resolved.Count },
                IsReversible = false,
                ReverseOp = null,
            });
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "RemoveAppComponents",
                Detail = $"Removed {succeededCount}/{resolved.Count} components from app {parsedAppId}",
                Success = succeededCount == resolved.Count,
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        }

        return outcomes.AsReadOnly();
    }
}
