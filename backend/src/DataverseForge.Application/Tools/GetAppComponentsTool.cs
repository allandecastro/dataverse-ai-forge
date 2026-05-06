using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetAppComponentsTool(IDataverseClient dataverseClient)
{
    [Description("List the components currently attached to a model-driven app. "
        + "Read-only. Returns a flat list of {kind, id, name, logicalName} entries — entities "
        + "have a logicalName, others have null. Merges two storage paths: the "
        + "appmodulecomponent join table (for apps we created via the API) and the descriptor "
        + "JSON on the appmodule itself (for maker-portal apps), so both flows answer reliably. "
        + "Useful mid-flow to check what's already in the app before adding/removing components.")]
    public async Task<IReadOnlyList<AppModuleComponentRef>> GetAppComponentsAsync(
        [Description("appmoduleid (GUID) — get from get_apps_list.")] Guid appId,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetAppComponentsAsync(appId, descriptorJson: null, cancellationToken);
    }
}
