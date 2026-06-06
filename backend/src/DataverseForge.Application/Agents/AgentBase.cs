using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Agents;

/// <summary>
/// Shared base for the eight specialized agents. The non-streaming
/// <see cref="ChatAsync"/> turn — inject ProjectState into the system prompt,
/// run one tool-enabled chat round, append the reply to history, stamp activity
/// — was byte-for-byte identical across all of them. Concrete agents now only
/// declare their <see cref="Name"/> and <see cref="SystemPrompt"/>.
///
/// Note: this <c>ChatAsync</c> path is exercised by <c>AgentOrchestrator</c>
/// and the orchestration tests. Production SSE traffic runs through
/// <c>OrchestratingChatClient</c>, which reads <see cref="SystemPrompt"/> and
/// <see cref="Tools"/> off this same abstraction (no reflection).
/// </summary>
public abstract class AgentBase(
    IChatClient chatClient,
    IProjectStateStore projectStateStore,
    IConversationStore conversationStore,
    ISessionContext sessionContext,
    IEnumerable<AITool> tools,
    IReadOnlyList<string> handoffTargets,
    ILogger logger) : IAgent
{
    private static readonly JsonSerializerOptions StateJsonOptions = new() { WriteIndented = true };

    public abstract string Name { get; }

    public abstract string SystemPrompt { get; }

    public IReadOnlyList<string> HandoffTargets { get; } = handoffTargets;

    public IReadOnlyList<AITool> Tools { get; } = tools as IReadOnlyList<AITool> ?? tools.ToList();

    public async Task<Dtos.ChatResponse> ChatAsync(string sessionId, string message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Processing chat for session {SessionId} via {AgentName}", sessionId, Name);

        sessionContext.SessionId = sessionId;

        var projectState = await projectStateStore.GetOrCreateAsync(sessionId, cancellationToken);
        var history = await conversationStore.GetOrCreateAsync(sessionId, cancellationToken);

        var injectedPrompt = SystemPrompt.Replace(
            "[INJECTED AT RUNTIME]",
            JsonSerializer.Serialize(projectState, StateJsonOptions));

        // Ensure the system message is first (replace if present, insert if not).
        if (history.Count > 0 && history[0].Role == ChatRole.System)
        {
            history[0] = new ChatMessage(ChatRole.System, injectedPrompt);
        }
        else
        {
            history.Insert(0, new ChatMessage(ChatRole.System, injectedPrompt));
        }

        history.Add(new ChatMessage(ChatRole.User, message));

        var options = new ChatOptions { Tools = [.. Tools] };

        var response = await chatClient.GetResponseAsync(history, options, cancellationToken);

        foreach (var msg in response.Messages)
        {
            history.Add(msg);
        }

        projectState.LastActivityAt = DateTime.UtcNow;
        await projectStateStore.SaveAsync(sessionId, projectState, cancellationToken);

        var responseText = response.Messages
            .Where(m => m.Role == ChatRole.Assistant)
            .SelectMany(m => m.Contents.OfType<TextContent>())
            .Select(tc => tc.Text)
            .LastOrDefault() ?? string.Empty;

        logger.LogInformation("Completed chat for session {SessionId} via {AgentName}, response length: {Length}",
            sessionId, Name, responseText.Length);

        return new Dtos.ChatResponse(sessionId, responseText, projectState);
    }

    public Task<ProjectState> GetProjectStateAsync(string sessionId, CancellationToken cancellationToken = default)
        => projectStateStore.GetOrCreateAsync(sessionId, cancellationToken);
}
