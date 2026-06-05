using DataverseForge.Application.Contracts;
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
    ILogger<SolutionArchitectAgent> logger)
    : AgentBase(chatClient, projectStateStore, conversationStore, sessionContext, tools, handoffTargets, logger)
{
    public override string Name => AgentNames.SolutionArchitect;
    public override string SystemPrompt => SolutionArchitectPrompt.SystemPrompt;
}
