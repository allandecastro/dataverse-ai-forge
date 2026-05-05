using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetAppDetailsTool(IDataverseClient dataverseClient)
{
    [Description("Read a single model-driven app's metadata (appmodule row). Read-only. "
        + "Returns the same fields as get_apps_list plus the descriptor blob and configxml. "
        + "Returns null when the app does not exist (caller should surface that to the user). "
        + "Use get_app_full instead if you also want components, sitemap, and assigned roles.")]
    public async Task<AppModuleDetails?> GetAppDetailsAsync(
        [Description("The appmoduleid (GUID) of the app to read. Get it from get_apps_list.")] Guid appId,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetAppAsync(appId, cancellationToken);
    }
}
