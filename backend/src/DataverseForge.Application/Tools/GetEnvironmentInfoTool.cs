using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetEnvironmentInfoTool(IDataverseClient dataverseClient)
{
    [Description("Get Dataverse environment information including organization name, URL, base language, version, region, currency, and audit status.")]
    public async Task<EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken)
    {
        return await dataverseClient.GetEnvironmentInfoAsync(cancellationToken);
    }
}
