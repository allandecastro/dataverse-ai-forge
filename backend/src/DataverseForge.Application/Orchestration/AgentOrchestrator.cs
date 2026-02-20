using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Domain;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Orchestration;

public class AgentOrchestrator(
    IEnumerable<IAgent> agents,
    IProjectStateStore projectStateStore,
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

        // Check if a handoff occurred during the agent's turn
        var updatedState = await projectStateStore.GetOrCreateAsync(sessionId, cancellationToken);
        if (updatedState.ActiveAgentName != activeAgentName)
        {
            logger.LogInformation("Handoff detected: {FromAgent} → {ToAgent} for session {SessionId}",
                activeAgentName, updatedState.ActiveAgentName, sessionId);
        }

        return response;
    }

    public async Task<ProjectState> GetProjectStateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return await projectStateStore.GetOrCreateAsync(sessionId, cancellationToken);
    }
}
