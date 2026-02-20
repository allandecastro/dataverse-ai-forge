using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToDataModelBuilderTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the Data Model Builder agent to create tables, fields, and relationships. "
        + "Use this when you have a finalized schema design and need to execute it in Dataverse. "
        + "Include the schema specification so the builder knows exactly what to create.")]
    public async Task<string> HandoffToDataModelBuilderAsync(
        [Description("The schema specification describing the tables, fields, and relationships to create")] string schemaSpec,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.ActiveAgentName = AgentNames.DataModelBuilder;
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        return "Handoff to Data Model Builder initiated with schema specification.";
    }
}
