using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Orchestration;

public class AgentOrchestrator(
    IEnumerable<IAgent> agents,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<AgentOrchestrator> logger)
{
    private readonly Dictionary<string, IAgent> _agentsByName = agents.ToDictionary(a => a.Name);

    public async Task<ChatResponse> ChatAsync(string sessionId, string message, CancellationToken cancellationToken = default)
    {
        var projectState = await projectStateStore.GetOrCreateAsync(sessionId, cancellationToken);
        var activeAgentName = projectState.ActiveAgentName;

        if (!_agentsByName.TryGetValue(activeAgentName, out var agent))
        {
            logger.LogWarning("Active agent '{AgentName}' not found for session {SessionId}. Falling back to SolutionArchitect.",
                activeAgentName, sessionId);
            agent = _agentsByName[AgentNames.SolutionArchitect];
            projectState.ActiveAgentName = AgentNames.SolutionArchitect;
            await projectStateStore.SaveAsync(sessionId, projectState, cancellationToken);
        }

        logger.LogInformation("Routing message for session {SessionId} to agent {AgentName}", sessionId, activeAgentName);

        var response = await agent.ChatAsync(sessionId, message, cancellationToken);

        // Defensive recovery: if the agent emitted a pseudo-handoff (the
        // entire reply is text like `handoff_to_solution_architect(...)` with
        // no real tool call attached), trigger the real handoff server-side
        // and rewrite the chat content to a natural-language acknowledgement.
        // Catches the residual cases where the prompt golden rule slips.
        var recoveredText = await PseudoHandoffRecovery.RecoverAsync(
            response.Response, projectStateStore, sessionContext, logger, cancellationToken);

        // Check if a handoff occurred during the agent's turn
        var updatedState = await projectStateStore.GetOrCreateAsync(sessionId, cancellationToken);
        if (updatedState.ActiveAgentName != activeAgentName)
        {
            logger.LogInformation("Handoff detected: {FromAgent} → {ToAgent} for session {SessionId}",
                activeAgentName, updatedState.ActiveAgentName, sessionId);
        }

        if (!ReferenceEquals(recoveredText, response.Response))
        {
            return new ChatResponse(sessionId, recoveredText, updatedState);
        }
        return response;
    }

    public async Task<ProjectState> GetProjectStateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return await projectStateStore.GetOrCreateAsync(sessionId, cancellationToken);
    }
}
