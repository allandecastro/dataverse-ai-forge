using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Infrastructure.AzureAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure;

public static partial class DependencyInjection
{
    public static IServiceCollection AddDocumentaryAgent(this IServiceCollection services)
    {
        services.AddScoped<DocumentaryAgent>(sp =>
        {
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();

            var handoffToSaTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                // ── KB management ────────────────────────────────────────
                AIFunctionFactory.Create(sp.GetRequiredService<ListSourcesTool>().ListSourcesAsync),
                AIFunctionFactory.Create(sp.GetRequiredService<IngestDocumentsTool>().IngestDocumentsAsync),
                AIFunctionFactory.Create(sp.GetRequiredService<DeleteSourceTool>().DeleteSourceAsync),
                // ── KB analysis ──────────────────────────────────────────
                AIFunctionFactory.Create(sp.GetRequiredService<SearchContextTool>().SearchContextAsync),
                AIFunctionFactory.Create(sp.GetRequiredService<CompareSourcesTool>().CompareSourcesAsync),
                AIFunctionFactory.Create(sp.GetRequiredService<SearchConflictTool>().SearchConflictAsync),
                // ── Routing ──────────────────────────────────────────────
                AIFunctionFactory.Create(handoffToSaTool.HandoffToSolutionArchitectAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect }.AsReadOnly();

            return new DocumentaryAgent(
                sp.GetRequiredService<ChatClientFactory>().GetClientForAgent(AgentNames.DocumentaryAgent),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<DocumentaryAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<DocumentaryAgent>());

        return services;
    }
}
