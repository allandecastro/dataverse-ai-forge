using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToDataPopulationTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Hand off to the Data Population Agent for seeding reference data or generating test records. "
        + "Use when tables need to be populated with initial or test data.")]
    public async Task<string> HandoffToDataPopulationAsync(
        [Description("Brief context about what data needs to be populated")] string context,
        CancellationToken cancellationToken)
    {
        return await HandoffTool.ExecuteHandoffAsync(projectStateStore, sessionContext, AgentNames.DataPopulation, "Data Population", context, cancellationToken);
    }
}
