using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class AttachSiteMapToAppTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<AttachSiteMapToAppTool> logger)
{
    [Description("Attach a sitemap to a model-driven app via the bound AddAppComponents action. "
        + "After this, validate_app should report no 'no sitemap' issue and the app becomes "
        + "openable in the maker portal. Direct (no plan/execute split) — single low-risk write "
        + "after the sitemap exists. Idempotent: re-attaching the same sitemap is a no-op.")]
    public async Task<string> AttachSiteMapToAppAsync(
        [Description("appmoduleid (GUID) of the app to attach the sitemap to.")] string appId,
        [Description("sitemapid (GUID) of the sitemap to attach.")] string siteMapId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(appId, out var parsedAppId) || parsedAppId == Guid.Empty)
            return "ERROR: appId must be a valid GUID.";
        if (!Guid.TryParse(siteMapId, out var parsedSiteMapId) || parsedSiteMapId == Guid.Empty)
            return "ERROR: siteMapId must be a valid GUID.";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            // The client returns per-ref outcomes — for a single-ref attach,
            // a non-Succeeded outcome is the same as a thrown error from the
            // caller's POV. Surface it via the same ERROR: shape.
            var refOutcomes = await dataverseClient.AddAppComponentsAsync(
                parsedAppId,
                new[] { new AppComponentRef("sitemap", parsedSiteMapId) },
                cancellationToken);
            var sitemapOutcome = refOutcomes.FirstOrDefault();
            if (sitemapOutcome is { Succeeded: false })
            {
                logger.LogError("AttachSiteMapToApp ref outcome failed: {Error}", sitemapOutcome.Error);
                return $"ERROR: Failed to attach sitemap {parsedSiteMapId} to app {parsedAppId}: {sitemapOutcome.Error}.";
            }

            // Update the in-session sitemap record with the attached app id
            // so the sidebar can show the attachment.
            var sm = state.SiteMaps.FirstOrDefault(s => s.SiteMapId == parsedSiteMapId);
            if (sm is not null)
            {
                state.SiteMaps.Remove(sm);
                state.SiteMaps.Add(sm with { AttachedToAppId = parsedAppId });
            }

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "AttachSiteMapToApp",
                Parameters = new { appId = parsedAppId, siteMapId = parsedSiteMapId },
                Result = new { appId = parsedAppId, siteMapId = parsedSiteMapId },
                IsReversible = false,
                ReverseOp = null,
            });

            state.AuditLog.Add(AuditEntry.Ok(AgentNames.UIBuilder, "AttachSiteMapToApp", $"Attached sitemap {parsedSiteMapId} to app {parsedAppId}"));

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
            return $"Sitemap {parsedSiteMapId} attached to app {parsedAppId}. Run publish_app({parsedAppId}) "
                + "and then validate_app to confirm the app can be opened.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "AttachSiteMapToApp failed: appId={AppId}, siteMapId={SiteMapId}", parsedAppId, parsedSiteMapId);
            return $"ERROR: Failed to attach sitemap {parsedSiteMapId} to app {parsedAppId}: {ex.Message}.";
        }
    }
}
