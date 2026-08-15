using DataverseForge.Application.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Agents;

public class DocumentaryAgent(
    IChatClient chatClient,
    IProjectStateStore projectStateStore,
    IConversationStore conversationStore,
    ISessionContext sessionContext,
    IEnumerable<AITool> tools,
    IReadOnlyList<string> handoffTargets,
    ILogger<DocumentaryAgent> logger)
    : AgentBase(chatClient, projectStateStore, conversationStore, sessionContext, tools, handoffTargets, logger)
{
    public override string Name => AgentNames.DocumentaryAgent;
    public override string SystemPrompt => DocumentaryAgentPrompt.SystemPrompt;
}
