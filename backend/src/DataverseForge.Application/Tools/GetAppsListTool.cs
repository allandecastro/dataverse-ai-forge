using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetAppsListTool(IDataverseClient dataverseClient)
{
    [Description("List model-driven apps (appmodule rows) in the Dataverse environment. "
        + "Read-only. Returns appModuleId, name, uniqueName, description, clientType, "
        + "navigationType, statecode, statuscode, isManaged, publishedOn for each. "
        + "By default only published / active apps are returned — pass includeUnpublished=true "
        + "to also include statecode=1 (deactivated) apps. Use this before get_app_details / "
        + "get_app_full to find an app's GUID.")]
    public async Task<IReadOnlyList<AppModuleInfo>> GetAppsListAsync(
        [Description("Set true to include deactivated apps (statecode=1). Default false.")] bool includeUnpublished,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetAppsListAsync(includeUnpublished, cancellationToken);
    }
}
