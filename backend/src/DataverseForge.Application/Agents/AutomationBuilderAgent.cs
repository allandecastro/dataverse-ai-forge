using DataverseForge.Application.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Agents;

/// <summary>
/// Manages Dataverse Business Process Flow + Business Rule lifecycle
/// (rename / activate / deactivate / delete). Creation deferred until
/// XAML templates can be validated against a real Dataverse env.
/// Classic workflows / custom actions / Power Automate are out of scope.
/// </summary>
public class AutomationBuilderAgent(
    IChatClient chatClient,
    IProjectStateStore projectStateStore,
    IConversationStore conversationStore,
    ISessionContext sessionContext,
    IEnumerable<AITool> tools,
    IReadOnlyList<string> handoffTargets,
    ILogger<AutomationBuilderAgent> logger)
    : AgentBase(chatClient, projectStateStore, conversationStore, sessionContext, tools, handoffTargets, logger)
{
    public override string Name => AgentNames.AutomationBuilder;
    public override string SystemPrompt => AutomationBuilderPrompt.SystemPrompt;
}
