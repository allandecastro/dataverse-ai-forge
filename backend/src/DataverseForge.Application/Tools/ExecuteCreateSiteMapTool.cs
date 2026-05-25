using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateSiteMapTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateSiteMapTool> logger)
{
    [Description("Execute the creation of a Dataverse sitemap. Pass the planId from "
        + "plan_create_sitemap — the OData JSON payload (with embedded XML) was stored server-side. "
        + "Atomically adds the sitemap to the active solution via the MSCRM.SolutionUniqueName header. "
        + "AFTER success, call attach_sitemap_to_app to wire the sitemap to a specific app.")]
    public async Task<string> ExecuteCreateSiteMapAsync(
        [Description("planId returned by plan_create_sitemap — must match exactly. Payload stored server-side.")] string planId,
        [Description("siteMapName for audit logging (must match the plan).")] string siteMapName,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ExecuteCreateSiteMap executing: planId={PlanId}, name={Name}", planId, siteMapName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_create_sitemap_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_sitemap. Fix the spec and re-plan.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. DO NOT retry.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_create_sitemap first and pass its planId verbatim.";
        }

        try
        {
            var solutionUniqueName = string.IsNullOrWhiteSpace(state.SolutionUniqueName)
                ? null
                : state.SolutionUniqueName;
            var solutionNote = solutionUniqueName is null
                ? " WARNING: no active solution selected — the sitemap is in the default solution."
                : $" Added to solution '{solutionUniqueName}' atomically.";

            var siteMapId = await dataverseClient.CreateSiteMapAsync(webApiPayloadJson, solutionUniqueName, cancellationToken);

            // F-23: re-read state right before mutate+save.
            var uniqueName = ExtractUniqueName(webApiPayloadJson);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.PendingPlanTimestamps.Remove(planId);
                fresh.SiteMaps.Add(new CreatedSiteMap(siteMapId, uniqueName, AttachedToAppId: null, DateTime.UtcNow));
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    ToolName = "ExecuteCreateSiteMap",
                    Parameters = new { siteMapName, planId },
                    Result = new { siteMapId },
                    IsReversible = false,    // sitemap delete is a separate path; keep simple for now
                    ReverseOp = null,
                });
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "ExecuteCreateSiteMap",
                    Detail = $"Created sitemap '{siteMapName}' (id={siteMapId})",
                    Success = true,
                });
            }, cancellationToken);

            logger.LogInformation("ExecuteCreateSiteMap succeeded: siteMapId={SiteMapId}", siteMapId);
            return $"Sitemap '{siteMapName}' created with SiteMapId={siteMapId}.{solutionNote} "
                + "Next step: call attach_sitemap_to_app(appId, siteMapId) to wire the sitemap to a model-driven app.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ExecuteCreateSiteMap failed: name={Name}", siteMapName);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "ExecuteCreateSiteMap",
                    Detail = $"Failed to create sitemap '{siteMapName}': {ex.Message}",
                    Success = false,
                });
            }, cancellationToken);
            return $"ERROR: Failed to create sitemap '{siteMapName}': {ex.Message}.";
        }
    }

    private static string ExtractUniqueName(string payloadJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("sitemapnameunique", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.String
                ? u.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
    }
}
