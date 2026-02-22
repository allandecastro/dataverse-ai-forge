using System.ClientModel;
using Azure.AI.OpenAI;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Orchestration;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Infrastructure.AzureOpenAi;
using DataverseForge.Infrastructure.Dataverse;
using DataverseForge.Infrastructure.AgUi;
using DataverseForge.Infrastructure.GitHub;
using DataverseForge.Infrastructure.Stores;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDataverseClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DataverseOptions>(
            configuration.GetSection(DataverseOptions.SectionName));

        services.AddTransient<DataverseAuthHandler>();

        services.AddHttpClient<IDataverseClient, DataverseClient>((sp, client) =>
        {
            var options = configuration
                .GetSection(DataverseOptions.SectionName)
                .Get<DataverseOptions>()!;

            client.BaseAddress = new Uri(options.EnvironmentUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        })
        .AddHttpMessageHandler<DataverseAuthHandler>();

        return services;
    }

    public static IServiceCollection AddAzureOpenAiChatClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureOpenAiOptions>(
            configuration.GetSection(AzureOpenAiOptions.SectionName));

        var aoaiOptions = configuration
            .GetSection(AzureOpenAiOptions.SectionName)
            .Get<AzureOpenAiOptions>()!;

        var azureClient = new AzureOpenAIClient(
            new Uri(aoaiOptions.Endpoint),
            new ApiKeyCredential(aoaiOptions.ApiKey));

        var openAiChatClient = azureClient.GetChatClient(aoaiOptions.Gpt4oDeploymentName);

        var chatClient = new ChatClientBuilder(openAiChatClient.AsIChatClient())
            .UseFunctionInvocation()
            .Build();

        services.AddSingleton<IChatClient>(chatClient);

        return services;
    }

    public static IServiceCollection AddStores(this IServiceCollection services)
    {
        services.AddSingleton<IProjectStateStore, InMemoryProjectStateStore>();
        services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        return services;
    }

    public static IServiceCollection AddSessionContext(this IServiceCollection services)
    {
        services.AddScoped<ISessionContext, SessionContext>();
        return services;
    }

    public static IServiceCollection AddSolutionArchitect(this IServiceCollection services)
    {
        services.AddScoped<SolutionArchitectAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();

            var envInfoTool = new GetEnvironmentInfoTool(dataverseClient);
            var solutionsListTool = new GetSolutionsListTool(dataverseClient);
            var handoffToBaTool = new HandoffToBusinessAnalystTool(projectStateStore, sessionContext);
            var handoffToBuilderTool = new HandoffToDataModelBuilderTool(projectStateStore, sessionContext);
            var publishersTool = new GetPublishersTool(dataverseClient);
            var tablesListTool = new GetTablesListTool(dataverseClient);
            var tableDetailsTool = new GetTableDetailsTool(dataverseClient);
            var validateSchemaTool = new ValidateSchemaNameTool();
            var checkOobTool = new CheckOobTableCoverageTool();
            var generateErdTool = new GenerateErdMermaidTool(projectStateStore, sessionContext);
            var handoffToReviewerTool = new HandoffToReviewerTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);
            var handoffToDocumentorTool = new HandoffToDocumentorTool(projectStateStore, sessionContext);
            var handoffToUIBuilderTool = new HandoffToUIBuilderTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(envInfoTool.GetEnvironmentInfoAsync),
                AIFunctionFactory.Create(solutionsListTool.GetSolutionsListAsync),
                AIFunctionFactory.Create(handoffToBaTool.HandoffToBusinessAnalystAsync),
                AIFunctionFactory.Create(handoffToBuilderTool.HandoffToDataModelBuilderAsync),
                AIFunctionFactory.Create(publishersTool.GetPublishersAsync),
                AIFunctionFactory.Create(tablesListTool.GetTablesListAsync),
                AIFunctionFactory.Create(tableDetailsTool.GetTableDetailsAsync),
                AIFunctionFactory.Create(validateSchemaTool.ValidateSchemaNameAsync),
                AIFunctionFactory.Create(checkOobTool.CheckOobTableCoverageAsync),
                AIFunctionFactory.Create(generateErdTool.GenerateErdMermaidAsync),
                AIFunctionFactory.Create(handoffToReviewerTool.HandoffToReviewerAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync),
                AIFunctionFactory.Create(handoffToDocumentorTool.HandoffToDocumentorAsync),
                AIFunctionFactory.Create(handoffToUIBuilderTool.HandoffToUIBuilderAsync)
            };

            var handoffTargets = new List<string> { AgentNames.BusinessAnalyst, AgentNames.DataModelBuilder, AgentNames.Reviewer, AgentNames.Tester, AgentNames.Documentor, AgentNames.UIBuilder }.AsReadOnly();

            return new SolutionArchitectAgent(
                sp.GetRequiredService<IChatClient>(),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<SolutionArchitectAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<SolutionArchitectAgent>());

        return services;
    }

    public static IServiceCollection AddBusinessAnalyst(this IServiceCollection services)
    {
        services.AddScoped<BusinessAnalystAgent>(sp =>
        {
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync)
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect }.AsReadOnly();

            return new BusinessAnalystAgent(
                sp.GetRequiredService<IChatClient>(),
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

    public static IServiceCollection AddDataModelBuilder(this IServiceCollection services)
    {
        services.AddScoped<DataModelBuilderAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();

            var planCreateTableTool = new PlanCreateTableTool();
            var executeCreateTableTool = new ExecuteCreateTableTool(dataverseClient, projectStateStore, sessionContext);
            var planCreateFieldTool = new PlanCreateFieldTool();
            var executeCreateFieldTool = new ExecuteCreateFieldTool(dataverseClient, projectStateStore, sessionContext);
            var planCreateRelTool = new PlanCreateOneToManyRelationshipTool();
            var executeCreateRelTool = new ExecuteCreateOneToManyRelationshipTool(dataverseClient, projectStateStore, sessionContext);
            var planCreateM2MTool = new PlanCreateManyToManyRelationshipTool();
            var executeCreateM2MTool = new ExecuteCreateManyToManyRelationshipTool(dataverseClient, projectStateStore, sessionContext);
            var planCreateGlobalOsTool = new PlanCreateGlobalOptionSetTool();
            var executeCreateGlobalOsTool = new ExecuteCreateGlobalOptionSetTool(dataverseClient, projectStateStore, sessionContext);
            var planAddOptionTool = new PlanAddOptionToOptionSetTool();
            var executeAddOptionTool = new ExecuteAddOptionToOptionSetTool(dataverseClient, projectStateStore, sessionContext);
            var planCreateAltKeyTool = new PlanCreateAlternateKeyTool();
            var executeCreateAltKeyTool = new ExecuteCreateAlternateKeyTool(dataverseClient, projectStateStore, sessionContext);
            var planModifyFieldTool = new PlanModifyFieldTool();
            var executeModifyFieldTool = new ExecuteModifyFieldTool(dataverseClient, projectStateStore, sessionContext);
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);
            var handoffToReviewerTool = new HandoffToReviewerTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(planCreateTableTool.PlanCreateTableAsync),
                AIFunctionFactory.Create(executeCreateTableTool.ExecuteCreateTableAsync),
                AIFunctionFactory.Create(planCreateFieldTool.PlanCreateFieldAsync),
                AIFunctionFactory.Create(executeCreateFieldTool.ExecuteCreateFieldAsync),
                AIFunctionFactory.Create(planCreateRelTool.PlanCreateOneToManyRelationshipAsync),
                AIFunctionFactory.Create(executeCreateRelTool.ExecuteCreateOneToManyRelationshipAsync),
                AIFunctionFactory.Create(planCreateM2MTool.PlanCreateManyToManyRelationshipAsync),
                AIFunctionFactory.Create(executeCreateM2MTool.ExecuteCreateManyToManyRelationshipAsync),
                AIFunctionFactory.Create(planCreateGlobalOsTool.PlanCreateGlobalOptionSetAsync),
                AIFunctionFactory.Create(executeCreateGlobalOsTool.ExecuteCreateGlobalOptionSetAsync),
                AIFunctionFactory.Create(planAddOptionTool.PlanAddOptionToOptionSetAsync),
                AIFunctionFactory.Create(executeAddOptionTool.ExecuteAddOptionToOptionSetAsync),
                AIFunctionFactory.Create(planCreateAltKeyTool.PlanCreateAlternateKeyAsync),
                AIFunctionFactory.Create(executeCreateAltKeyTool.ExecuteCreateAlternateKeyAsync),
                AIFunctionFactory.Create(planModifyFieldTool.PlanModifyFieldAsync),
                AIFunctionFactory.Create(executeModifyFieldTool.ExecuteModifyFieldAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                AIFunctionFactory.Create(handoffToReviewerTool.HandoffToReviewerAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync)
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect, AgentNames.Reviewer, AgentNames.Tester }.AsReadOnly();

            return new DataModelBuilderAgent(
                sp.GetRequiredService<IChatClient>(),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<DataModelBuilderAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<DataModelBuilderAgent>());

        return services;
    }

    public static IServiceCollection AddUIBuilder(this IServiceCollection services)
    {
        services.AddScoped<UIBuilderAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();

            var getFormsListTool = new GetFormsListTool(dataverseClient);
            var getFormDefinitionTool = new GetFormDefinitionTool(dataverseClient);
            var planCreateFormTool = new PlanCreateFormTool();
            var executeCreateFormTool = new ExecuteCreateFormTool(dataverseClient, projectStateStore, sessionContext);
            var planCreateViewTool = new PlanCreateViewTool();
            var executeCreateViewTool = new ExecuteCreateViewTool(dataverseClient, projectStateStore, sessionContext);
            var planCreateDashboardTool = new PlanCreateDashboardTool();
            var executeCreateDashboardTool = new ExecuteCreateDashboardTool(dataverseClient, projectStateStore, sessionContext);
            var planCreateModelDrivenAppTool = new PlanCreateModelDrivenAppTool();
            var executeCreateModelDrivenAppTool = new ExecuteCreateModelDrivenAppTool(dataverseClient, projectStateStore, sessionContext);
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);
            var handoffToReviewerTool = new HandoffToReviewerTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(getFormsListTool.GetFormsListAsync),
                AIFunctionFactory.Create(getFormDefinitionTool.GetFormDefinitionAsync),
                AIFunctionFactory.Create(planCreateFormTool.PlanCreateFormAsync),
                AIFunctionFactory.Create(executeCreateFormTool.ExecuteCreateFormAsync),
                AIFunctionFactory.Create(planCreateViewTool.PlanCreateViewAsync),
                AIFunctionFactory.Create(executeCreateViewTool.ExecuteCreateViewAsync),
                AIFunctionFactory.Create(planCreateDashboardTool.PlanCreateDashboardAsync),
                AIFunctionFactory.Create(executeCreateDashboardTool.ExecuteCreateDashboardAsync),
                AIFunctionFactory.Create(planCreateModelDrivenAppTool.PlanCreateModelDrivenAppAsync),
                AIFunctionFactory.Create(executeCreateModelDrivenAppTool.ExecuteCreateModelDrivenAppAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                AIFunctionFactory.Create(handoffToReviewerTool.HandoffToReviewerAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync)
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect, AgentNames.Reviewer, AgentNames.Tester }.AsReadOnly();

            return new UIBuilderAgent(
                sp.GetRequiredService<IChatClient>(),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<UIBuilderAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<UIBuilderAgent>());

        return services;
    }

    public static IServiceCollection AddReviewer(this IServiceCollection services)
    {
        services.AddScoped<ReviewerAgent>(sp =>
        {
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();
            var validateSchemaDesignTool = new ValidateSchemaDesignTool();
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(validateSchemaDesignTool.ValidateSchemaDesignAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync)
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect }.AsReadOnly();

            return new ReviewerAgent(
                sp.GetRequiredService<IChatClient>(),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<ReviewerAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<ReviewerAgent>());

        return services;
    }

    public static IServiceCollection AddTester(this IServiceCollection services)
    {
        services.AddScoped<TesterAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();
            var verifyTableTool = new VerifyTableCreationTool(dataverseClient);
            var verifyFieldTool = new VerifyFieldCreationTool(dataverseClient);
            var verifyRelTool = new VerifyRelationshipCreationTool(dataverseClient);
            var verifyFormTool = new VerifyFormCreationTool(dataverseClient);
            var verifyViewTool = new VerifyViewCreationTool(dataverseClient);
            var fullValidationTool = new RunFullValidationTool(dataverseClient, projectStateStore, sessionContext);
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(verifyTableTool.VerifyTableCreationAsync),
                AIFunctionFactory.Create(verifyFieldTool.VerifyFieldCreationAsync),
                AIFunctionFactory.Create(verifyRelTool.VerifyRelationshipCreationAsync),
                AIFunctionFactory.Create(verifyFormTool.VerifyFormCreationAsync),
                AIFunctionFactory.Create(verifyViewTool.VerifyViewCreationAsync),
                AIFunctionFactory.Create(fullValidationTool.RunFullValidationAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync)
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect }.AsReadOnly();

            return new TesterAgent(
                sp.GetRequiredService<IChatClient>(),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<TesterAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<TesterAgent>());

        return services;
    }

    public static IServiceCollection AddGitHubClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var token = configuration["GitHub:Token"];
        services.AddSingleton<IGitHubClient>(sp =>
            new OctokitGitHubClient(token, sp.GetRequiredService<ILogger<OctokitGitHubClient>>()));
        return services;
    }

    public static IServiceCollection AddDocumentor(this IServiceCollection services)
    {
        services.AddScoped<DocumentorAgent>(sp =>
        {
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();
            var dataDictionaryTool = new GenerateDataDictionaryTool(projectStateStore, sessionContext);
            var changelogTool = new GenerateChangelogEntryTool(projectStateStore, sessionContext);
            var erdDocTool = new GenerateErdDocTool(projectStateStore, sessionContext);
            var pushDocsTool = new PushDocumentationToGitHubTool(sp.GetRequiredService<IGitHubClient>());
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(dataDictionaryTool.GenerateDataDictionaryAsync),
                AIFunctionFactory.Create(changelogTool.GenerateChangelogEntryAsync),
                AIFunctionFactory.Create(erdDocTool.GenerateErdDocAsync),
                AIFunctionFactory.Create(pushDocsTool.PushDocumentationToGitHubAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync)
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect }.AsReadOnly();

            return new DocumentorAgent(
                sp.GetRequiredService<IChatClient>(),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<DocumentorAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<DocumentorAgent>());

        return services;
    }

    public static IServiceCollection AddAgentOrchestrator(this IServiceCollection services)
    {
        services.AddScoped<AgentOrchestrator>();
        return services;
    }

    public static IServiceCollection AddAgUi(this IServiceCollection services)
    {
        services.AddSingleton<OrchestratingChatClient>(sp =>
        {
            var innerClient = sp.GetRequiredService<IChatClient>();
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var logger = sp.GetRequiredService<ILogger<OrchestratingChatClient>>();
            return new OrchestratingChatClient(scopeFactory, innerClient, logger);
        });
        return services;
    }
}
