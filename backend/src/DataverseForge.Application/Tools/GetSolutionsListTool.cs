using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetSolutionsListTool(IDataverseClient dataverseClient)
{
    [Description("Get list of unmanaged Dataverse solutions in the environment, including solution ID, unique name, display name, version, publisher, and component count.")]
    public async Task<IReadOnlyList<SolutionInfo>> GetSolutionsListAsync(CancellationToken cancellationToken)
    {
        return await dataverseClient.GetSolutionsListAsync(cancellationToken);
    }
}
