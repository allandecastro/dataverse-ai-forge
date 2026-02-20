using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Domain;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Agents;

public class SolutionArchitectAgent(
    IChatClient chatClient,
    IProjectStateStore projectStateStore,
    IConversationStore conversationStore,
    ISessionContext sessionContext,
    IEnumerable<AITool> tools,
    IReadOnlyList<string> handoffTargets,
    ILogger<SolutionArchitectAgent> logger) : IAgent
{
    public string Name => AgentNames.SolutionArchitect;
    public IReadOnlyList<string> HandoffTargets { get; } = handoffTargets;

    public async Task<Dtos.ChatResponse> ChatAsync(string sessionId, string message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Processing chat for session {SessionId} via {AgentName}", sessionId, Name);

        sessionContext.SessionId = sessionId;

        var projectState = await projectStateStore.GetOrCreateAsync(sessionId, cancellationToken);
        var history = await conversationStore.GetOrCreateAsync(sessionId, cancellationToken);

        // Build system prompt with injected project state
        var systemPrompt = SolutionArchitectPrompt.SystemPrompt.Replace(
            "[INJECTED AT RUNTIME]",
            JsonSerializer.Serialize(projectState, new JsonSerializerOptions { WriteIndented = true }));

        // Ensure system message is first (replace if already present, add if not)
        if (history.Count > 0 && history[0].Role == ChatRole.System)
        {
            history[0] = new ChatMessage(ChatRole.System, systemPrompt);
        }
        else
        {
            history.Insert(0, new ChatMessage(ChatRole.System, systemPrompt));
        }

        // Append user message
        history.Add(new ChatMessage(ChatRole.User, message));

        var options = new ChatOptions
        {
            Tools = [.. tools]
        };

        var response = await chatClient.GetResponseAsync(history, options, cancellationToken);

        // Append assistant response messages to history
        foreach (var msg in response.Messages)
        {
            history.Add(msg);
        }

        // Update last activity timestamp
        projectState.LastActivityAt = DateTime.UtcNow;
        await projectStateStore.SaveAsync(sessionId, projectState, cancellationToken);

        var responseText = response.Messages
            .Where(m => m.Role == ChatRole.Assistant)
            .SelectMany(m => m.Contents.OfType<TextContent>())
            .Select(tc => tc.Text)
            .LastOrDefault() ?? string.Empty;

        logger.LogInformation("Completed chat for session {SessionId}, response length: {Length}", sessionId, responseText.Length);

        return new Dtos.ChatResponse(sessionId, responseText, projectState);
    }

    public async Task<ProjectState> GetProjectStateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return await projectStateStore.GetOrCreateAsync(sessionId, cancellationToken);
    }
}
