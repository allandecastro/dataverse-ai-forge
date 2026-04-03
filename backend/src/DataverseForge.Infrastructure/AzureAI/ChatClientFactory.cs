using Azure;
using Azure.AI.Inference;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.AzureAI;

/// <summary>
/// Creates and caches per-model <see cref="IChatClient"/> instances using
/// Azure AI Inference API. Supports both Azure OpenAI (cognitiveservices)
/// and Azure AI Foundry (services.ai.azure.com) endpoints.
/// Supports lazy initialization and hot-reload via <see cref="Reconfigure"/>.
/// </summary>
public sealed class ChatClientFactory
{
    private readonly object _lock = new();
    private readonly ILogger<ChatClientFactory> _logger;
    private Dictionary<string, IChatClient> _clients = new();
    private AzureAiOptions _options;
    private bool _configured;

    public ChatClientFactory(AzureAiOptions options, ILogger<ChatClientFactory> logger)
    {
        _options = options;
        _logger = logger;

        if (IsConfigValid(options))
        {
            BuildClients(options);
        }
        else
        {
            _logger.LogWarning("Azure AI credentials not configured — agents will not work until configured via Settings");
        }
    }

    public bool IsConfigured => _configured;

    public void Reconfigure(AzureAiOptions options)
    {
        lock (_lock)
        {
            _options = options;
            if (IsConfigValid(options))
            {
                BuildClients(options);
                _logger.LogInformation("ChatClientFactory reconfigured with endpoint {Endpoint}", options.Endpoint);
            }
            else
            {
                _configured = false;
                _clients = new();
                _logger.LogWarning("ChatClientFactory reconfigured with invalid/empty credentials");
            }
        }
    }

    public IChatClient GetClientForAgent(string agentName)
    {
        lock (_lock)
        {
            if (!_configured)
                throw new InvalidOperationException(
                    "Azure AI is not configured. Please set your credentials in Settings.");

            var model = _options.GetModelForAgent(agentName);
            if (_clients.TryGetValue(model, out var client))
                return client;

            var newClient = CreateClient(_options.Endpoint, _options.ApiKey, model);
            _clients[model] = newClient;
            return newClient;
        }
    }

    public IChatClient DefaultClient
    {
        get
        {
            lock (_lock)
            {
                if (!_configured)
                    throw new InvalidOperationException(
                        "Azure AI is not configured. Please set your credentials in Settings.");

                return _clients[_options.DefaultModel];
            }
        }
    }

    private void BuildClients(AzureAiOptions options)
    {
        var clients = new Dictionary<string, IChatClient>();
        var uniqueModels = options.AgentModels.Values
            .Append(options.DefaultModel)
            .Distinct();

        foreach (var model in uniqueModels)
        {
            clients[model] = CreateClient(options.Endpoint, options.ApiKey, model);
        }

        _clients = clients;
        _configured = true;
    }

    private IChatClient CreateClient(string endpoint, string apiKey, string model)
    {
        var trimmedEndpoint = endpoint.TrimEnd('/');
        var isAzureOpenAi = trimmedEndpoint.Contains("cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase)
                         || trimmedEndpoint.Contains("openai.azure.com", StringComparison.OrdinalIgnoreCase);

        ChatCompletionsClient inferenceClient;

        if (isAzureOpenAi)
        {
            // Azure OpenAI: endpoint must point to the specific deployment
            // e.g., https://xxx.cognitiveservices.azure.com/openai/deployments/gpt-4o
            var deploymentUrl = $"{trimmedEndpoint}/openai/deployments/{model}";
            _logger.LogInformation("Creating Azure OpenAI client: {Url}", deploymentUrl);
            inferenceClient = new ChatCompletionsClient(
                new Uri(deploymentUrl),
                new AzureKeyCredential(apiKey));

            // For Azure OpenAI via Inference SDK, model name is not passed to AsIChatClient
            return new ChatClientBuilder(inferenceClient.AsIChatClient())
                .UseFunctionInvocation()
                .Build();
        }
        else
        {
            // Azure AI Foundry: base endpoint + model name
            _logger.LogInformation("Creating Azure AI Foundry client: {Endpoint}, model={Model}", trimmedEndpoint, model);
            inferenceClient = new ChatCompletionsClient(
                new Uri(trimmedEndpoint),
                new AzureKeyCredential(apiKey));

            return new ChatClientBuilder(inferenceClient.AsIChatClient(model))
                .UseFunctionInvocation()
                .Build();
        }
    }

    private static bool IsConfigValid(AzureAiOptions options) =>
        !string.IsNullOrWhiteSpace(options.Endpoint)
        && !string.IsNullOrWhiteSpace(options.ApiKey)
        && !string.IsNullOrWhiteSpace(options.DefaultModel)
        && !options.Endpoint.Contains("YOUR-PROJECT");
}
