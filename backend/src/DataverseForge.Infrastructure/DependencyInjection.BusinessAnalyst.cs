using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Orchestration;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Infrastructure.AzureAI;
using DataverseForge.Infrastructure.Dataverse;
using DataverseForge.Infrastructure.AgUi;
using DataverseForge.Infrastructure.Middleware;
using DataverseForge.Infrastructure.Settings;
using DataverseForge.Infrastructure.Stores;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure;

public static partial class DependencyInjection
{
    public static IServiceCollection AddBusinessAnalyst(this IServiceCollection services)
    {
        services.AddScoped<BusinessAnalystAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);

            // Read-only tools so BA can investigate / clarify against live Dataverse state
            // before bouncing back to the Architect.
            var envInfoTool = new GetEnvironmentInfoTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<GetEnvironmentInfoTool>>());
            var solutionsListTool = new GetSolutionsListTool(dataverseClient, sp.GetRequiredService<ILogger<GetSolutionsListTool>>());
            var publishersTool = new GetPublishersTool(dataverseClient, sp.GetRequiredService<ILogger<GetPublishersTool>>());
            var tablesListTool = new GetTablesListTool(dataverseClient, sp.GetRequiredService<ILogger<GetTablesListTool>>());
            var tableDetailsTool = new GetTableDetailsTool(dataverseClient);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(envInfoTool.GetEnvironmentInfoAsync),
                AIFunctionFactory.Create(solutionsListTool.GetSolutionsListAsync),
                AIFunctionFactory.Create(publishersTool.GetPublishersAsync),
                AIFunctionFactory.Create(tablesListTool.GetTablesListAsync),
                AIFunctionFactory.Create(tableDetailsTool.GetTableDetailsAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                // RAG — inline grounding (search stays on BA to avoid round-trips for every context lookup)
                AIFunctionFactory.Create(sp.GetRequiredService<SearchContextTool>().SearchContextAsync),
                // KB management is delegated to the Documentary Agent
                AIFunctionFactory.Create(new HandoffToDocumentaryAgentTool(projectStateStore, sessionContext).HandoffToDocumentaryAgentAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect, AgentNames.DocumentaryAgent }.AsReadOnly();

            return new BusinessAnalystAgent(
                sp.GetRequiredService<ChatClientFactory>().GetClientForAgent(AgentNames.BusinessAnalyst),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<BusinessAnalystAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<BusinessAnalystAgent>());

        return services;
    }
}
