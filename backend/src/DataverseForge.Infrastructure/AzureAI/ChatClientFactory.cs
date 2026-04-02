using Azure;
using Azure.AI.Inference;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.AzureAI;

/// <summary>
/// Creates and caches per-model <see cref="IChatClient"/> instances using
/// Azure AI Foundry Inference API. Supports lazy initialization (starts
/// without credentials) and hot-reload via <see cref="Reconfigure"/>.
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

    /// <summary>
    /// Whether the factory has been configured with valid credentials.
    /// </summary>
    public bool IsConfigured => _configured;

    /// <summary>
    /// Rebuild all clients with new options. Called when user saves settings.
    /// </summary>
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

    /// <summary>
    /// Returns the <see cref="IChatClient"/> configured for the given agent.
    /// Falls back to the default model if the agent has no explicit mapping.
    /// Throws if not configured.
    /// </summary>
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

            // Model not in cache — create on demand
            var newClient = CreateClient(_options.Endpoint, _options.ApiKey, model);
            _clients[model] = newClient;
            return newClient;
        }
    }

    /// <summary>
    /// Returns the <see cref="IChatClient"/> for the default model.
    /// Throws if not configured.
    /// </summary>
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

    private static IChatClient CreateClient(string endpoint, string apiKey, string model)
    {
        var inferenceClient = new ChatCompletionsClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));

        return new ChatClientBuilder(inferenceClient.AsIChatClient(model))
            .UseFunctionInvocation()
            .Build();
    }

    private static bool IsConfigValid(AzureAiOptions options) =>
        !string.IsNullOrWhiteSpace(options.Endpoint)
        && !string.IsNullOrWhiteSpace(options.ApiKey)
        && !string.IsNullOrWhiteSpace(options.DefaultModel)
        && options.Endpoint != "https://YOUR-PROJECT.services.ai.azure.com/";
}
