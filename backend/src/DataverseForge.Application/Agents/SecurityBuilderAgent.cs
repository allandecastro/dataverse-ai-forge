using DataverseForge.Application.Contracts;
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
    ILogger<SecurityBuilderAgent> logger)
    : AgentBase(chatClient, projectStateStore, conversationStore, sessionContext, tools, handoffTargets, logger)
{
    public override string Name => AgentNames.SecurityBuilder;
    public override string SystemPrompt => SecurityBuilderPrompt.SystemPrompt;
}
