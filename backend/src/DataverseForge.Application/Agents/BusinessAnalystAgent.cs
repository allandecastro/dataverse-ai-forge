using DataverseForge.Application.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Agents;

public class BusinessAnalystAgent(
    IChatClient chatClient,
    IProjectStateStore projectStateStore,
    IConversationStore conversationStore,
    ISessionContext sessionContext,
    IEnumerable<AITool> tools,
    IReadOnlyList<string> handoffTargets,
    ILogger<BusinessAnalystAgent> logger)
    : AgentBase(chatClient, projectStateStore, conversationStore, sessionContext, tools, handoffTargets, logger)
{
    public override string Name => AgentNames.BusinessAnalyst;
    public override string SystemPrompt => BusinessAnalystPrompt.SystemPrompt;
}
