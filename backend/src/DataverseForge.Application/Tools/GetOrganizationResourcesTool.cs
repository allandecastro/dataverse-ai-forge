using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Reads the organization's capacity / utilization snapshot from the Dataverse
/// Web API <c>RetrieveOrganizationResources</c> function. Read-only — useful
/// when the user asks how close they are to a platform cap (custom entities,
/// storage, active users, published workflows).
/// </summary>
public class GetOrganizationResourcesTool(
    IDataverseClient dataverseClient,
    ILogger<GetOrganizationResourcesTool> logger)
{
    [Description("Read the organization's capacity utilization from Dataverse: current and maximum "
        + "counts of active users, non-interactive users, custom entities, published workflows, and "
        + "storage usage. Use this when the user asks 'how many tables can I still create?', 'are we "
        + "running out of storage?', 'how close are we to the user cap?', or before suggesting a large "
        + "schema change. Read-only — no approval needed.")]
    public async Task<OrganizationResources> GetOrganizationResourcesAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("GetOrganizationResources executing");
        return await dataverseClient.RetrieveOrganizationResourcesAsync(cancellationToken);
    }
}
