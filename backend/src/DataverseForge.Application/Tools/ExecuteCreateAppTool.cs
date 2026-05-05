using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateAppTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateAppTool> logger)
{
    [Description("Execute the creation of a model-driven app (appmodule). Pass the planId from "
        + "plan_create_app — the OData JSON payload was stored server-side and will be loaded "
        + "automatically. Do NOT construct or pass the payload yourself. After success, this tool "
        + "publishes the new app and adds it to the active solution via the MSCRM.SolutionUniqueName "
        + "header (atomic — no eventual-consistency retry needed).")]
    public async Task<string> ExecuteCreateAppAsync(
        [Description("planId returned by plan_create_app — must match exactly. Payload stored server-side.")] string planId,
        [Description("Display name (must match the plan, used for audit).")] string name,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ExecuteCreateApp executing: planId={PlanId}, name={Name}", planId, name);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_create_app_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_app. Fix the inputs and re-plan.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"App '{name}' is already in the environment. DO NOT retry.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_create_app first and pass its planId verbatim.";
        }

        try
        {
            // MSCRM.SolutionUniqueName header → atomic create + solution-add.
            // Falls back to default solution when no active solution selected
            // (with a warning so the user knows).
            var solutionUniqueName = string.IsNullOrWhiteSpace(state.SolutionUniqueName)
                ? null
                : state.SolutionUniqueName;
            var solutionNote = solutionUniqueName is null
                ? " WARNING: no active solution selected — the app is in the default solution."
                : $" Added to solution '{solutionUniqueName}' atomically.";

            var appId = await dataverseClient.CreateAppAsync(webApiPayloadJson, solutionUniqueName, cancellationToken);
            state.ExecutedPlans[planId] = webApiPayloadJson;
            state.PendingPlans.Remove(planId);
            state.PendingPlanTimestamps.Remove(planId);

            // Scoped publish so the new app is visible to consumers immediately.
            // PublishApp is cheaper than PublishAll for a single new app.
            var publishNote = string.Empty;
            try
            {
                await dataverseClient.PublishAppAsync(appId, cancellationToken);
                publishNote = " Published.";
            }
            catch (Exception pubEx) when (pubEx is not OperationCanceledException)
            {
                logger.LogWarning(pubEx, "PublishApp after create failed for {AppId}", appId);
                publishNote = $" (publish failed: {pubEx.Message} — admin can publish manually).";
            }

            state.Apps.Add(new CreatedApp(appId, name, ExtractUniqueName(webApiPayloadJson), DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "ExecuteCreateApp",
                Parameters = new { name, planId },
                Result = new { appId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteApp",
                    Parameters = new { appId },
                    Warning = "Deleting an app removes its sitemap, component associations, and role assignments. The referenced tables/forms/views remain.",
                    RiskLevel = RiskLevel.High
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateApp",
                Detail = $"Created app '{name}' (id={appId})",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateApp succeeded: appId={AppId}, name={Name}", appId, name);
            return $"App '{name}' created with AppModuleId={appId}.{solutionNote}{publishNote} "
                + "Next step: attach a sitemap (Phase 4) — ValidateApp will report 'no sitemap' until you do.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ExecuteCreateApp failed: name={Name}", name);
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateApp",
                Detail = $"Failed to create app '{name}': {ex.Message}",
                Success = false
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
            return $"ERROR: Failed to create app '{name}': {ex.Message}.";
        }
    }

    private static string ExtractUniqueName(string payloadJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("uniquename", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.String
                ? u.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
    }
}
