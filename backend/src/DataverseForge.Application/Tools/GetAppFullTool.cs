using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetAppFullTool(IDataverseClient dataverseClient)
{
    [Description("Read a model-driven app + its components + sitemap + assigned roles in one "
        + "parallel fan-out. Read-only. Returns AppModuleFullDetails: app metadata, "
        + "components (entities/forms/views/charts/dashboards/sitemap, branched by @odata.type), "
        + "the sitemap currently attached (or null), and the security roles granting access. "
        + "Returns null when the app does not exist. Use this when the user asks 'what's inside <app>?'")]
    public async Task<AppModuleFullDetails?> GetAppFullAsync(
        [Description("The appmoduleid (GUID) of the app to read. Get it from get_apps_list.")] Guid appId,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetAppFullAsync(appId, cancellationToken);
    }
}
