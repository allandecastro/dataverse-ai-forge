using Azure;
using Azure.AI.Inference;
using Microsoft.Extensions.AI;

namespace DataverseForge.Infrastructure.AzureAI;

/// <summary>
/// Creates and caches per-model <see cref="IChatClient"/> instances using
/// Azure AI Foundry Inference API. Each unique model gets one client with
/// function invocation middleware.
/// </summary>
public sealed class ChatClientFactory
{
    private readonly AzureAiOptions _options;
    private readonly Dictionary<string, IChatClient> _clients = new();

    public ChatClientFactory(AzureAiOptions options)
    {
        _options = options;

        // Build one IChatClient per unique model deployment
        var uniqueModels = options.AgentModels.Values
            .Append(options.DefaultModel)
            .Distinct();

        foreach (var model in uniqueModels)
        {
            var inferenceClient = new ChatCompletionsClient(
                new Uri(options.Endpoint),
                new AzureKeyCredential(options.ApiKey));

            var chatClient = new ChatClientBuilder(inferenceClient.AsIChatClient(model))
                .UseFunctionInvocation()
                .Build();

            _clients[model] = chatClient;
        }
    }

    /// <summary>
    /// Returns the <see cref="IChatClient"/> configured for the given agent.
    /// Falls back to the default model if the agent has no explicit mapping.
    /// </summary>
    public IChatClient GetClientForAgent(string agentName)
    {
        var model = _options.GetModelForAgent(agentName);
        return _clients[model];
    }

    /// <summary>
    /// Returns the <see cref="IChatClient"/> for the default model.
    /// </summary>
    public IChatClient DefaultClient => _clients[_options.DefaultModel];
}
