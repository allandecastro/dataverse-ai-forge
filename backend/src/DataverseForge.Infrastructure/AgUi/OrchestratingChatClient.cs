using System.Runtime.CompilerServices;
using System.Text.Json;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Infrastructure.AzureAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.AgUi;

/// <summary>
/// IChatClient wrapper that resolves the active agent per request,
/// injects the correct system prompt + tools, and delegates to the
/// per-agent IChatClient via ChatClientFactory for streaming.
/// </summary>
public sealed class OrchestratingChatClient(
    IServiceScopeFactory scopeFactory,
    ChatClientFactory chatClientFactory,
    ILogger<OrchestratingChatClient> logger) : IChatClient
{
    private static readonly Dictionary<string, Func<IServiceProvider, (string SystemPrompt, IEnumerable<AITool> Tools)>> AgentResolvers = new()
    {
        [AgentNames.SolutionArchitect] = sp =>
        {
            var agent = sp.GetRequiredService<SolutionArchitectAgent>();
            return (SolutionArchitectPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.BusinessAnalyst] = sp =>
        {
            var agent = sp.GetRequiredService<BusinessAnalystAgent>();
            return (BusinessAnalystPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.DataModelBuilder] = sp =>
        {
            var agent = sp.GetRequiredService<DataModelBuilderAgent>();
            return (DataModelBuilderPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.Reviewer] = sp =>
        {
            var agent = sp.GetRequiredService<ReviewerAgent>();
            return (ReviewerPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.Tester] = sp =>
        {
            var agent = sp.GetRequiredService<TesterAgent>();
            return (TesterPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.Documentor] = sp =>
        {
            var agent = sp.GetRequiredService<DocumentorAgent>();
            return (DocumentorPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.UIBuilder] = sp =>
        {
            var agent = sp.GetRequiredService<UIBuilderAgent>();
            return (UIBuilderPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.AutomationBuilder] = sp =>
        {
            var agent = sp.GetRequiredService<AutomationBuilderAgent>();
            return (AutomationBuilderPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.SecurityBuilder] = sp =>
        {
            var agent = sp.GetRequiredService<SecurityBuilderAgent>();
            return (SecurityBuilderPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.DataPopulation] = sp =>
        {
            var agent = sp.GetRequiredService<DataPopulationAgent>();
            return (DataPopulationPrompt.SystemPrompt, GetAgentTools(agent));
        }
    };

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
        var conversationStore = sp.GetRequiredService<IConversationStore>();
        var sessionContext = sp.GetRequiredService<ISessionContext>();

        // Extract threadId from options or generate one
        var threadId = options?.AdditionalProperties?.TryGetValue("threadId", out var tid) == true
            ? tid?.ToString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");

        sessionContext.SessionId = threadId;

        var projectState = await projectStateStore.GetOrCreateAsync(threadId, cancellationToken);
        var activeAgentName = projectState.ActiveAgentName;

        logger.LogInformation("Streaming request for session {SessionId}, active agent: {AgentName}", threadId, activeAgentName);

        if (!AgentResolvers.TryGetValue(activeAgentName, out var resolver))
        {
            logger.LogWarning("Agent '{AgentName}' not found, falling back to SolutionArchitect", activeAgentName);
            activeAgentName = AgentNames.SolutionArchitect;
            resolver = AgentResolvers[activeAgentName];
            projectState.ActiveAgentName = activeAgentName;
            await projectStateStore.SaveAsync(threadId, projectState, cancellationToken);
        }

        var (systemPrompt, tools) = resolver(sp);

        // Inject project state into system prompt
        var injectedPrompt = systemPrompt.Replace(
            "[INJECTED AT RUNTIME]",
            JsonSerializer.Serialize(projectState, new JsonSerializerOptions { WriteIndented = true }));

        // Build message list with system prompt
        var history = await conversationStore.GetOrCreateAsync(threadId, cancellationToken);

        if (history.Count > 0 && history[0].Role == ChatRole.System)
        {
            history[0] = new ChatMessage(ChatRole.System, injectedPrompt);
        }
        else
        {
            history.Insert(0, new ChatMessage(ChatRole.System, injectedPrompt));
        }

        // Append incoming user messages (skip system messages already in history)
        foreach (var msg in chatMessages)
        {
            if (msg.Role != ChatRole.System)
            {
                history.Add(msg);
            }
        }

        var chatOptions = new ChatOptions
        {
            Tools = [.. tools]
        };

        // Yield a synthetic update with the active agent name so downstream
        // consumers (AgUiSseWriter) can emit STATE_DELTA without coupling.
        yield return new ChatResponseUpdate
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["forge_agent_name"] = activeAgentName
            }
        };

        var innerClient = chatClientFactory.GetClientForAgent(activeAgentName);

        await foreach (var update in innerClient.GetStreamingResponseAsync(history, chatOptions, cancellationToken))
        {
            yield return update;
        }

        // Validate handoff topology if agent changed during the turn
        var updatedState = await projectStateStore.GetOrCreateAsync(threadId, cancellationToken);
        if (updatedState.ActiveAgentName != activeAgentName)
        {
            var validation = Application.Orchestration.HandoffValidator.Validate(activeAgentName, updatedState.ActiveAgentName);
            if (!validation.IsValid)
            {
                logger.LogWarning("Invalid handoff blocked: {FromAgent} → {ToAgent}: {Error}",
                    activeAgentName, updatedState.ActiveAgentName, validation.ErrorMessage);
                updatedState.ActiveAgentName = activeAgentName;
                updatedState.AuditLog.Add(new Domain.Models.AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = activeAgentName,
                    Action = "HandoffBlocked",
                    Detail = validation.ErrorMessage ?? "Invalid handoff",
                    Success = false
                });
            }
            else
            {
                logger.LogInformation("Handoff validated: {FromAgent} → {ToAgent}", activeAgentName, updatedState.ActiveAgentName);
            }
        }

        // Update state after completion
        updatedState.LastActivityAt = DateTime.UtcNow;
        await projectStateStore.SaveAsync(threadId, updatedState, cancellationToken);

        // Persist conversation history
        await conversationStore.SaveAsync(threadId, history, cancellationToken);
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await chatClientFactory.DefaultClient.GetResponseAsync(chatMessages, options, cancellationToken);
    }

    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return chatClientFactory.DefaultClient.GetService(serviceType, serviceKey);
    }

    private static IEnumerable<AITool> GetAgentTools(IAgent agent)
    {
        // All agent constructors accept IEnumerable<AITool> tools
        // The primary constructor parameters are stored as synthesized fields
        var toolsField = agent.GetType().GetFields(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(f => f.FieldType.IsAssignableTo(typeof(IEnumerable<AITool>)));

        if (toolsField != null)
        {
            return (IEnumerable<AITool>)toolsField.GetValue(agent)!;
        }

        return [];
    }
}
