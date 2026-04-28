using System.ClientModel;
using Azure;
using Azure.AI.Inference;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.AzureAI;

/// <summary>
/// Creates and caches per-model <see cref="IChatClient"/> instances.
/// Auto-detects the endpoint type:
/// - cognitiveservices.azure.com / openai.azure.com → uses Azure.AI.OpenAI SDK
/// - services.ai.azure.com (Foundry) → uses Azure.AI.Inference SDK
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
        // Normalize: strip path/query — keep only scheme + host
        // Handles users pasting the full chat completions URL by mistake
        var baseEndpoint = endpoint;
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            baseEndpoint = $"{uri.Scheme}://{uri.Host}";
            if (!uri.IsDefaultPort) baseEndpoint += $":{uri.Port}";
        }
        var trimmedEndpoint = baseEndpoint.TrimEnd('/');
        var isAzureOpenAi = trimmedEndpoint.Contains("cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase)
                         || trimmedEndpoint.Contains("openai.azure.com", StringComparison.OrdinalIgnoreCase);

        IChatClient innerClient;

        if (isAzureOpenAi)
        {
            // Azure OpenAI: use AzureOpenAIClient + GetChatClient(deployment name)
            _logger.LogInformation("Creating Azure OpenAI client: {Endpoint}, deployment={Model}", trimmedEndpoint, model);
            var azureClient = new AzureOpenAIClient(
                new Uri(trimmedEndpoint),
                new ApiKeyCredential(apiKey));
            var openAiChatClient = azureClient.GetChatClient(model);
            innerClient = openAiChatClient.AsIChatClient();
        }
        else
        {
            // Azure AI Foundry: use ChatCompletionsClient with model in request body
            _logger.LogInformation("Creating Azure AI Foundry client: {Endpoint}, model={Model}", trimmedEndpoint, model);
            var inferenceClient = new ChatCompletionsClient(
                new Uri(trimmedEndpoint),
                new AzureKeyCredential(apiKey));
            innerClient = inferenceClient.AsIChatClient(model);
        }

        return new ChatClientBuilder(innerClient)
            .UseFunctionInvocation()
            .Build();
    }

    private static bool IsConfigValid(AzureAiOptions options) =>
        !string.IsNullOrWhiteSpace(options.Endpoint)
        && !string.IsNullOrWhiteSpace(options.ApiKey)
        && !string.IsNullOrWhiteSpace(options.DefaultModel)
        && !options.Endpoint.Contains("YOUR-PROJECT");
}
