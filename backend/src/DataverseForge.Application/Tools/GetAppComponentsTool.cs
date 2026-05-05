using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetAppComponentsTool(IDataverseClient dataverseClient)
{
    [Description("List the components currently attached to a model-driven app via the unbound "
        + "RetrieveAppComponents function. Read-only. Returns a flat list of {kind, id, name, "
        + "logicalName} entries — entities have a logicalName, others have null. Useful mid-flow "
        + "to check what's already in the app before adding/removing components. Same data as "
        + "get_app_full's components field, exposed standalone for cheaper round-trips.")]
    public async Task<IReadOnlyList<AppModuleComponentRef>> GetAppComponentsAsync(
        [Description("appmoduleid (GUID) — get from get_apps_list.")] Guid appId,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetAppComponentsAsync(appId, cancellationToken);
    }
}
