using DataverseForge.Application.Agents;
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
        // The canonical parameter is `context`, but the agent has been observed
        // calling this tool with `reason` / `summary` (the names other handoff
        // tools use) — when the binder can't find that parameter it rejects the
        // call and the model falls back to emitting the function name as plain
        // text in chat. Accepting all three names absorbs that variance.
        [Description("Brief context about what data needs to be populated")] string context = "",
        CancellationToken cancellationToken = default,
        [Description("ALIAS for `context`. Optional. Used only when `context` is empty.")] string reason = "",
        [Description("ALIAS for `context`. Optional. Used only when `context` and `reason` are empty.")] string summary = "")
    {
        var effective = HandoffTool.PickEffectiveSpec(context, reason, summary);
        return await HandoffTool.ExecuteHandoffAsync(projectStateStore, sessionContext, AgentNames.DataPopulation, "Data Population", effective, cancellationToken);
    }
}
