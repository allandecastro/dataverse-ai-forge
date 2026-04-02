using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Application.Orchestration;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using DataverseForge.Infrastructure;
using DataverseForge.Infrastructure.AgUi;
using DataverseForge.Infrastructure.AzureAI;
using DataverseForge.Infrastructure.Middleware;
using DataverseForge.Infrastructure.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ─── USER SETTINGS (highest-priority, hot-reloadable) ───
var userSettingsPath = Path.Combine(AppContext.BaseDirectory, "user-settings.json");
builder.Configuration.AddJsonFile(userSettingsPath, optional: true, reloadOnChange: true);

// ─── STRUCTURED LOGGING ───
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

// ─── SERVICES ───
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
builder.Services.AddDataverseClient(builder.Configuration);
builder.Services.AddAzureAiChatClients(builder.Configuration);
builder.Services.AddStores(builder.Configuration);
builder.Services.AddSessionContext();
builder.Services.AddSolutionArchitect();
builder.Services.AddBusinessAnalyst();
builder.Services.AddDataModelBuilder();
builder.Services.AddUIBuilder();
builder.Services.AddAutomationBuilder();
builder.Services.AddSecurityBuilder();
builder.Services.AddDataPopulation();
builder.Services.AddReviewer();
builder.Services.AddTester();
builder.Services.AddGitHubClient(builder.Configuration);
builder.Services.AddDocumentor();
builder.Services.AddAgentOrchestrator();
builder.Services.AddAgUi();
builder.Services.AddObservability(builder.Configuration);
builder.Services.AddSettingsServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ─── MIDDLEWARE PIPELINE ───
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseCors();

// ─── HEALTH + READINESS ───
app.MapGet("/healthz", async (
    IDataverseClient dataverseClient,
    IGitHubClient gitHubClient,
    IHttpClientFactory httpClientFactory,
    IOptions<AzureAiOptions> azureAiOptions,
    ILogger<Program> logger) =>
{
    var checks = new Dictionary<string, object>
    {
        ["status"] = "healthy",
        ["timestamp"] = DateTime.UtcNow,
        ["version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"
    };

    try
    {
        var envInfo = await dataverseClient.GetEnvironmentInfoAsync();
        checks["dataverse"] = new { status = "connected", environment = envInfo.OrganizationName };
    }
    catch (Exception ex)
    {
        checks["dataverse"] = new { status = "unavailable", error = ex.Message };
        checks["status"] = "degraded";
        logger.LogWarning(ex, "Health check: Dataverse unreachable");
    }

    // Azure AI check
    try
    {
        var endpoint = azureAiOptions.Value.Endpoint.TrimEnd('/');
        var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        var response = await httpClient.GetAsync($"{endpoint}/models?api-version=2024-05-01-preview");
        checks["azureAi"] = new { status = "reachable", endpoint };
    }
    catch (Exception ex)
    {
        checks["azureAi"] = new { status = "unavailable", endpoint = azureAiOptions.Value.Endpoint };
        checks["status"] = "degraded";
        logger.LogWarning(ex, "Health check: Azure AI unreachable");
    }

    // GitHub check
    try
    {
        var ghConnected = await gitHubClient.CheckConnectivityAsync();
        checks["github"] = new { status = ghConnected ? "connected" : "unavailable" };
        if (!ghConnected) checks["status"] = "degraded";
    }
    catch (Exception ex)
    {
        checks["github"] = new { status = "unavailable" };
        checks["status"] = "degraded";
        logger.LogWarning(ex, "Health check: GitHub unreachable");
    }

    return Results.Ok(checks);
});

app.MapGet("/readyz", () => Results.Ok(new { status = "ready", timestamp = DateTime.UtcNow }));

// ─── CHAT ENDPOINTS ───
app.MapPost("/api/chat", async (ChatRequest request, AgentOrchestrator orchestrator, CancellationToken ct) =>
{
    var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N");
    var response = await orchestrator.ChatAsync(sessionId, request.Message, ct);
    return Results.Ok(response);
});

// CopilotKit remote endpoint info — returns available agents so the frontend can discover them
app.MapPost("/api/copilotkit/info", () => Results.Ok(new
{
    actions = Array.Empty<object>(),
    agents = new[] { new { name = "dataverse_forge", description = "AI-powered Dataverse solution builder" } }
}));

app.MapPost("/api/copilotkit", async (HttpContext ctx, OrchestratingChatClient client, IProjectStateStore projectStateStore, ILogger<Program> logger, CancellationToken ct) =>
{
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body, cancellationToken: ct);

    var threadId = body.TryGetProperty("threadId", out var tid) ? tid.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
    var runId = body.TryGetProperty("runId", out var rid) ? rid.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");

    var messages = new List<ChatMessage>();
    if (body.TryGetProperty("messages", out var messagesArray))
    {
        foreach (var msg in messagesArray.EnumerateArray())
        {
            var role = msg.TryGetProperty("role", out var r) ? r.GetString() ?? "user" : "user";
            var content = msg.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
            messages.Add(new ChatMessage(new ChatRole(role), content));
        }
    }

    var options = new ChatOptions
    {
        AdditionalProperties = new AdditionalPropertiesDictionary
        {
            ["threadId"] = threadId
        }
    };

    var projectState = await projectStateStore.GetOrCreateAsync(threadId, ct);
    var agentName = projectState.ActiveAgentName;

    logger.LogInformation("SSE stream started: session={SessionId}, agent={Agent}, messages={MessageCount}",
        threadId, agentName, messages.Count);

    using var activity = ForgeActivitySource.Source.StartActivity("SSE Stream");
    activity?.SetTag("session.id", threadId);
    activity?.SetTag("agent.name", agentName);
    activity?.SetTag("message.count", messages.Count);

    var sw = Stopwatch.StartNew();

    // Build solution state for STATE_DELTA so the frontend can show live data
    var solutionState = new
    {
        solutionName = projectState.SolutionDisplayName,
        solutionPrefix = projectState.PublisherPrefix,
        solutionVersion = projectState.SolutionVersion,
        tables = projectState.Tables.Select(t => new
        {
            logicalName = t.LogicalName,
            displayName = t.DisplayName,
            fields = projectState.Fields
                .Where(f => f.TableLogicalName == t.LogicalName)
                .Select(f => new { logicalName = f.LogicalName, displayName = f.DisplayName, fieldType = f.FieldType })
                .ToArray()
        }).ToArray(),
        relationships = projectState.Relationships.Select(r => new
        {
            schemaName = r.SchemaName,
            primaryTable = r.PrimaryTable,
            relatedTable = r.RelatedTable,
            relationshipType = r.RelationshipType
        }).ToArray()
    };

    var stream = client.GetStreamingResponseAsync(messages, options, ct);
    await AgUiSseWriter.WriteStreamAsync(ctx.Response, threadId, runId, agentName, stream, ct, solutionState);

    sw.Stop();
    ForgeActivitySource.SseStreamDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("agent", agentName));
    ForgeActivitySource.AgentTurns.Add(1, new KeyValuePair<string, object?>("agent", agentName));

    logger.LogInformation("SSE stream completed: session={SessionId}, agent={Agent}, duration={Duration}ms",
        threadId, agentName, sw.ElapsedMilliseconds);
});

// ─── AUDIT LOG API ───
app.MapGet("/api/projects/{sessionId}/audit", async (
    string sessionId,
    IProjectStateStore projectStateStore,
    string? agent,
    string? action,
    bool? success,
    CancellationToken ct) =>
{
    var state = await projectStateStore.GetOrCreateAsync(sessionId, ct);
    IEnumerable<AuditEntry> entries = state.AuditLog;

    if (!string.IsNullOrEmpty(agent))
        entries = entries.Where(e => e.AgentName.Equals(agent, StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrEmpty(action))
        entries = entries.Where(e => e.Action.Contains(action, StringComparison.OrdinalIgnoreCase));
    if (success.HasValue)
        entries = entries.Where(e => e.Success == success.Value);

    return Results.Ok(new
    {
        sessionId,
        count = entries.Count(),
        entries = entries.OrderByDescending(e => e.Timestamp).ToList()
    });
});

app.MapGet("/api/projects/{sessionId}/operations", async (
    string sessionId,
    IProjectStateStore projectStateStore,
    CancellationToken ct) =>
{
    var state = await projectStateStore.GetOrCreateAsync(sessionId, ct);
    var operations = state.OperationStack.Select(op => new
    {
        op.OperationId,
        op.ExecutedAt,
        op.AgentName,
        op.ToolName,
        op.IsReversible,
        reverseToolName = op.ReverseOp?.ToolName,
        riskLevel = op.ReverseOp?.RiskLevel.ToString()
    }).ToList();

    return Results.Ok(new
    {
        sessionId,
        count = operations.Count,
        operations
    });
});

// ─── SESSION MANAGEMENT API ───
app.MapGet("/api/sessions", async (IProjectStateStore projectStateStore, CancellationToken ct) =>
{
    var sessions = await projectStateStore.ListSessionsAsync(ct);
    return Results.Ok(new { count = sessions.Count, sessions });
});

app.MapDelete("/api/sessions/{sessionId}", async (string sessionId, IProjectStateStore projectStateStore, CancellationToken ct) =>
{
    await projectStateStore.DeleteSessionAsync(sessionId, ct);
    return Results.NoContent();
});

// ─── SETTINGS API ───
app.MapGet("/api/settings", (UserSettingsService settingsService) =>
{
    var settings = settingsService.GetCurrentSettings();
    return Results.Ok(settings);
});

app.MapPut("/api/settings", async (SettingsDto dto, UserSettingsService settingsService, ChatClientFactory chatClientFactory) =>
{
    await settingsService.SaveSettingsAsync(dto);

    // Hot-reload AI clients if Azure AI credentials changed
    if (!string.IsNullOrWhiteSpace(dto.AzureAiEndpoint) && !dto.AzureAiApiKey.Contains("••••"))
    {
        chatClientFactory.Reconfigure(new AzureAiOptions
        {
            Endpoint = dto.AzureAiEndpoint,
            ApiKey = dto.AzureAiApiKey,
            DefaultModel = string.IsNullOrWhiteSpace(dto.AzureAiDefaultModel) ? "gpt-4o" : dto.AzureAiDefaultModel
        });
    }

    return Results.Ok(new { message = "Settings saved successfully" });
});

app.MapPost("/api/settings/test", async (TestConnectionRequest request, SettingsTestService testService) =>
{
    var result = await testService.TestAsync(request.Service, request.Settings);
    return Results.Ok(result);
});

app.Run();
