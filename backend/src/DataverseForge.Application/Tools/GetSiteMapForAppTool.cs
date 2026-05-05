using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetSiteMapForAppTool(IDataverseClient dataverseClient)
{
    [Description("Read the sitemap currently attached to a model-driven app via the "
        + "appmodule_sitemap M:N. Read-only. Returns sitemapId, sitemapName, sitemapNameUnique, "
        + "sitemapXml, isManaged. Returns null when no sitemap is attached (the app has no "
        + "navigation defined yet — that's the expected state for a freshly-created app).")]
    public async Task<SiteMapInfo?> GetSiteMapForAppAsync(
        [Description("The appmoduleid (GUID) of the app whose sitemap should be read.")] Guid appId,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetSiteMapForAppAsync(appId, cancellationToken);
    }
}
