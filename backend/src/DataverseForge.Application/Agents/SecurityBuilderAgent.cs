using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Agents;

/// <summary>
/// Owns Dataverse security-role CRUD: create with privilege matrix,
/// update via privilege delta, delete with safety pre-flight, bulk-grant
/// one privilege to N roles, optional RoleTemplate binding.
/// </summary>
public class SecurityBuilderAgent(
    IChatClient chatClient,
    IProjectStateStore projectStateStore,
    IConversationStore conversationStore,
    ISessionContext sessionContext,
    IEnumerable<AITool> tools,
    IReadOnlyList<string> handoffTargets,
    ILogger<SecurityBuilderAgent> logger) : IAgent
{
    public string Name => AgentNames.SecurityBuilder;
    public IReadOnlyList<string> HandoffTargets { get; } = handoffTargets;

    public async Task<Dtos.ChatResponse> ChatAsync(string sessionId, string message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Processing chat for session {SessionId} via {AgentName}", sessionId, Name);

        sessionContext.SessionId = sessionId;

        var projectState = await projectStateStore.GetOrCreateAsync(sessionId, cancellationToken);
        var history = await conversationStore.GetOrCreateAsync(sessionId, cancellationToken);

        var systemPrompt = SecurityBuilderPrompt.SystemPrompt.Replace(
            "[INJECTED AT RUNTIME]",
            JsonSerializer.Serialize(projectState, new JsonSerializerOptions { WriteIndented = true }));

        if (history.Count > 0 && history[0].Role == ChatRole.System)
        {
            history[0] = new ChatMessage(ChatRole.System, systemPrompt);
        }
        else
        {
            history.Insert(0, new ChatMessage(ChatRole.System, systemPrompt));
        }

        history.Add(new ChatMessage(ChatRole.User, message));

        var options = new ChatOptions
        {
            Tools = [.. tools]
        };

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
}
