using System.ClientModel;
using Azure.AI.OpenAI;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Infrastructure.AzureOpenAi;
using DataverseForge.Infrastructure.Dataverse;
using DataverseForge.Infrastructure.Stores;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    public static IServiceCollection AddSolutionArchitect(this IServiceCollection services)
    {
        services.AddScoped<SolutionArchitectAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var envInfoTool = new GetEnvironmentInfoTool(dataverseClient);
            var solutionsListTool = new GetSolutionsListTool(dataverseClient);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(envInfoTool.GetEnvironmentInfoAsync),
                AIFunctionFactory.Create(solutionsListTool.GetSolutionsListAsync)
            };

            return new SolutionArchitectAgent(
                sp.GetRequiredService<IChatClient>(),
                sp.GetRequiredService<IProjectStateStore>(),
                sp.GetRequiredService<IConversationStore>(),
                tools,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SolutionArchitectAgent>>());
        });

        return services;
    }
}
