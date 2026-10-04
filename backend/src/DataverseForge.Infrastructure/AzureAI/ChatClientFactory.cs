using System.ClientModel;
using Azure;
using Azure.AI.Inference;
using Azure.AI.OpenAI;
using DataverseForge.Application.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.AzureAI;

/// <summary>
/// Creates and caches per-model <see cref="IChatClient"/> instances.
/// Endpoint detection: cognitiveservices.azure.com / openai.azure.com →
/// Azure.AI.OpenAI; services.ai.azure.com → Azure.AI.Inference. Supports
/// hot-reload via <see cref="Reconfigure"/>. Implements <see cref="IDisposable"/>
/// to release SDK HTTP pipelines on shutdown and on every Reconfigure.
/// </summary>
public sealed class ChatClientFactory : IDisposable
{
    private readonly object _lock = new();
    private readonly ILogger<ChatClientFactory> _logger;
    private Dictionary<string, IChatClient> _clients = new();
    private AzureAiOptions _options;
    private bool _configured;
    private bool _disposed;

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
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Tear down the prior cohort — each IChatClient wraps an SDK HTTP pipeline.
            DisposeClients(_clients);
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

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            DisposeClients(_clients);
            _clients = new();
            _configured = false;
            _disposed = true;
        }
    }

    private void DisposeClients(Dictionary<string, IChatClient> clients)
    {
        foreach (var (model, client) in clients)
        {
            try
            {
                client.Dispose();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Disposal failures are non-fatal — log so a recurring leak is diagnosable.
                _logger.LogWarning(ex, "Failed to dispose cached chat client for model {Model}", model);
            }
        }
    }

    public IChatClient GetClientForAgent(string agentName)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
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
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_configured)
                    throw new InvalidOperationException(
                        "Azure AI is not configured. Please set your credentials in Settings.");

                return _clients[_options.DefaultModel];
            }
        }
    }

    private void BuildClients(AzureAiOptions options)
    {
        WarnOnUnknownAgentKeys(options);

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

    /// <summary>
    /// A typo'd key in <c>AzureAI:AgentModels</c> (e.g. "SolutionArchitec")
    /// silently never matches any agent, so that agent quietly uses the default
    /// model and the operator's override is ignored without a trace. Surface it
    /// as a warning instead of failing silently.
    /// </summary>
    private void WarnOnUnknownAgentKeys(AzureAiOptions options)
    {
        var unknown = options.AgentModels.Keys
            .Where(k => !AgentNames.All.Contains(k))
            .ToList();

        if (unknown.Count > 0)
        {
            _logger.LogWarning(
                "AzureAI:AgentModels has {Count} key(s) that match no known agent and will be ignored: {Keys}. Valid agent names: {Valid}",
                unknown.Count, string.Join(", ", unknown), string.Join(", ", AgentNames.All));
        }
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
            // Azure AI Foundry: use ChatCompletionsClient with model in request body.
            // Unified "services.ai.azure.com" resources serve the model-inference API
            // under /models (the real call is POST /models/chat/completions). The SDK
            // appends /chat/completions to whatever endpoint we hand it, so the base
            // must end in /models or every call 404s "Resource not found". Older
            // serverless *.models.ai.azure.com (MaaS) endpoints serve it at the root —
            // leave those untouched.
            var inferenceEndpoint = trimmedEndpoint.Contains("services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
                ? $"{trimmedEndpoint}/models"
                : trimmedEndpoint;
            _logger.LogInformation("Creating Azure AI Foundry client: {Endpoint}, model={Model}", inferenceEndpoint, model);
            var inferenceClient = new ChatCompletionsClient(
                new Uri(inferenceEndpoint),
                new AzureKeyCredential(apiKey));
            innerClient = inferenceClient.AsIChatClient(model);
        }

        return new ChatClientBuilder(innerClient)
            // IncludeDetailedErrors: a tool that throws hands the agent the exception
            // message instead of a bare "Function failed." Without it the agent can't
            // relay actionable errors (e.g. "this project has no Dataverse connection",
            // an embedding 404) and guesses at causes instead.
            .UseFunctionInvocation(configure: c =>
            {
                c.IncludeDetailedErrors = true;
                c.FunctionInvoker = InvokeAndStopAfterTurnEndingToolAsync;
            })
            .Build();
    }

    /// <summary>
    /// Runs a tool, then ends the agent's tool loop when the tool closes its turn:
    /// <list type="bullet">
    ///   <item>a successful handoff — the next agent takes over;</item>
    ///   <item>suggest_replies called on its own — the agent has written its message.</item>
    /// </list>
    /// Otherwise the model gets one more iteration and, observed repeatedly in the app,
    /// re-writes the message it already streamed ("I'm handing this to…" twice), which
    /// reads as "it says X takes over, then nothing happens".
    /// </summary>
    internal static async ValueTask<object?> InvokeAndStopAfterTurnEndingToolAsync(
        FunctionInvocationContext context, CancellationToken cancellationToken)
    {
        var result = await context.Function.InvokeAsync(context.Arguments, cancellationToken);
        var name = context.Function.Name;

        var handedOff = name.StartsWith("HandoffTo", StringComparison.Ordinal)
            && result?.ToString()?.StartsWith("Handoff to", StringComparison.Ordinal) == true;
        var onlySuggestedReplies = name.StartsWith("SuggestReplies", StringComparison.Ordinal)
            && context.FunctionCount == 1;

        if (handedOff || onlySuggestedReplies)
            context.Terminate = true;

        return result;
    }

    private static bool IsConfigValid(AzureAiOptions options) =>
        !string.IsNullOrWhiteSpace(options.Endpoint)
        && !string.IsNullOrWhiteSpace(options.ApiKey)
        && !string.IsNullOrWhiteSpace(options.DefaultModel)
        && !options.Endpoint.Contains("YOUR-PROJECT");
}
