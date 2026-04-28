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
    UserSettingsService settingsService,
    SettingsTestService testService,
    ILogger<Program> logger) =>
{
    // Pull the live settings (with real, unmasked secrets) and probe each service
    // through the same code path as Test Connection. That guarantees the health
    // badge can't disagree with the Test Connection result the user just saw.
    var settings = settingsService.GetCurrentSettings();
    settings.AzureAiApiKey = settingsService.GetRealApiKey("AzureAI:ApiKey") ?? string.Empty;
    settings.DataverseClientSecret = settingsService.GetRealApiKey("Dataverse:ClientSecret") ?? string.Empty;
    settings.GitHubToken = settingsService.GetRealApiKey("GitHub:Token") ?? string.Empty;

    var checks = new Dictionary<string, object>
    {
        ["status"] = "healthy",
        ["timestamp"] = DateTime.UtcNow,
        ["version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"
    };

    // Dataverse
    if (string.IsNullOrWhiteSpace(settings.DataverseEnvironmentUrl)
        || string.IsNullOrWhiteSpace(settings.DataverseClientId)
        || string.IsNullOrWhiteSpace(settings.DataverseClientSecret)
        || settings.DataverseEnvironmentUrl.Contains("YOUR-ORG"))
    {
        checks["dataverse"] = new { status = "unavailable", configured = false };
        checks["status"] = "degraded";
    }
    else
    {
        var dv = await testService.TestAsync("dataverse", settings);
        if (dv.Success)
        {
            checks["dataverse"] = new
            {
                status = "connected",
                configured = true,
                environment = dv.Environment,
                environmentUrl = dv.EnvironmentUrl
            };
        }
        else
        {
            checks["dataverse"] = new { status = "unavailable", configured = true, error = dv.Message };
            checks["status"] = "degraded";
            logger.LogInformation("Health: Dataverse probe failed — {Message}", dv.Message);
        }
    }

    // Azure AI
    if (string.IsNullOrWhiteSpace(settings.AzureAiEndpoint)
        || string.IsNullOrWhiteSpace(settings.AzureAiApiKey)
        || settings.AzureAiEndpoint.Contains("YOUR-PROJECT"))
    {
        checks["azureAi"] = new { status = "unavailable", configured = false };
        checks["status"] = "degraded";
    }
    else
    {
        var ai = await testService.TestAsync("azureai", settings);
        if (ai.Success)
        {
            checks["azureAi"] = new { status = "reachable", configured = true };
        }
        else
        {
            checks["azureAi"] = new { status = "unavailable", configured = true, error = ai.Message };
            checks["status"] = "degraded";
            logger.LogInformation("Health: Azure AI probe failed — {Message}", ai.Message);
        }
    }

    // GitHub
    if (string.IsNullOrWhiteSpace(settings.GitHubToken))
    {
        checks["github"] = new { status = "unavailable", configured = false };
        checks["status"] = "degraded";
    }
    else
    {
        var gh = await testService.TestAsync("github", settings);
        if (gh.Success)
        {
            checks["github"] = new { status = "connected", configured = true };
        }
        else
        {
            checks["github"] = new { status = "unavailable", configured = true, error = gh.Message };
            checks["status"] = "degraded";
            logger.LogInformation("Health: GitHub probe failed — {Message}", gh.Message);
        }
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

    // Build solution state for STATE_DELTA so the frontend can show live data.
    // Same shape used both for the initial snapshot and the post-stream refresh.
    static object BuildSolutionState(DataverseForge.Domain.Models.ProjectState s) => new
    {
        environmentName = s.EnvironmentName,
        environmentUrl = s.EnvironmentUrl,
        solutionName = s.SolutionDisplayName,
        solutionPrefix = s.PublisherPrefix,
        solutionVersion = s.SolutionVersion,
        tables = s.Tables.Select(t => new
        {
            logicalName = t.LogicalName,
            displayName = t.DisplayName,
            fields = s.Fields
                .Where(f => f.TableLogicalName == t.LogicalName)
                .Select(f => new { logicalName = f.LogicalName, displayName = f.DisplayName, fieldType = f.FieldType })
                .ToArray()
        }).ToArray(),
        relationships = s.Relationships.Select(r => new
        {
            schemaName = r.SchemaName,
            primaryTable = r.PrimaryTable,
            relatedTable = r.RelatedTable,
            relationshipType = r.RelationshipType
        }).ToArray()
    };
    var solutionState = BuildSolutionState(projectState);

    // Refresher: re-fetch ProjectState after the agent's tool calls finish so the
    // sidebar reflects SelectSolution / CreateSolution / ExecuteCreate* mutations.
    Func<Task<object>> refreshSolutionState = async () =>
    {
        var latest = await projectStateStore.GetOrCreateAsync(threadId, ct);
        return BuildSolutionState(latest);
    };

    try
    {
        var stream = client.GetStreamingResponseAsync(messages, options, ct);
        await AgUiSseWriter.WriteStreamAsync(ctx.Response, threadId, runId, agentName, stream, ct, solutionState, refreshSolutionState);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
    {
        // AI not configured — send friendly error as SSE text message
        await AgUiSseWriter.WriteErrorAsStreamAsync(ctx.Response, threadId, runId,
            "Azure AI is not configured. Please open Settings and enter your credentials.", ct);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SSE stream error: session={SessionId}, agent={Agent}", threadId, agentName);

        // If headers already sent (streaming started), we can't change status code.
        // Write the error as an SSE event so the frontend gets it.
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = 500;
        }
        else
        {
            await AgUiSseWriter.WriteErrorAsStreamAsync(ctx.Response, threadId, runId,
                $"Agent error: {ex.Message}", ct);
        }
    }

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

app.MapPut("/api/sessions/{sessionId}", async (
    string sessionId,
    RenameSessionDto dto,
    IProjectStateStore projectStateStore,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.ProjectName))
        return Results.BadRequest(new { error = "projectName is required" });

    var state = await projectStateStore.GetOrCreateAsync(sessionId, ct);
    state.ProjectName = dto.ProjectName.Trim();
    state.LastActivityAt = DateTime.UtcNow;
    await projectStateStore.SaveAsync(sessionId, state, ct);
    return Results.Ok(new { sessionId, projectName = state.ProjectName });
});

app.MapPost("/api/sessions", async (
    CreateSessionDto dto,
    IProjectStateStore projectStateStore,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.ProjectName))
        return Results.BadRequest(new { error = "projectName is required" });

    var sessionId = Guid.NewGuid().ToString("N");
    var state = await projectStateStore.GetOrCreateAsync(sessionId, ct);
    state.ProjectName = dto.ProjectName.Trim();
    state.CreatedAt = DateTime.UtcNow;
    state.LastActivityAt = DateTime.UtcNow;
    await projectStateStore.SaveAsync(sessionId, state, ct);
    return Results.Ok(new { sessionId, projectName = state.ProjectName });
});

// ─── SETTINGS API ───
app.MapGet("/api/settings", (UserSettingsService settingsService) =>
{
    var settings = settingsService.GetCurrentSettings();
    return Results.Ok(settings);
});

app.MapPut("/api/settings", async (SettingsDto dto, UserSettingsService settingsService, ChatClientFactory chatClientFactory, IConfiguration configuration) =>
{
    await settingsService.SaveSettingsAsync(dto);

    // Force-reload configuration so IOptionsMonitor.CurrentValue picks up
    // new values immediately (no debounce wait)
    if (configuration is IConfigurationRoot root)
    {
        root.Reload();
    }

    // Hot-reload AI clients — re-read the unmasked settings from the freshly saved file
    var freshSettings = settingsService.GetCurrentSettings();
    // GetCurrentSettings returns masked secrets, but we need the real API key.
    // Use the DTO value if it's not masked, otherwise skip reconfigure.
    var apiKey = dto.AzureAiApiKey.Contains("••••") ? null : dto.AzureAiApiKey;
    var endpoint = !string.IsNullOrWhiteSpace(dto.AzureAiEndpoint) ? dto.AzureAiEndpoint : freshSettings.AzureAiEndpoint;
    var model = !string.IsNullOrWhiteSpace(dto.AzureAiDefaultModel) ? dto.AzureAiDefaultModel : freshSettings.AzureAiDefaultModel;

    if (!string.IsNullOrWhiteSpace(endpoint) && apiKey != null)
    {
        chatClientFactory.Reconfigure(new AzureAiOptions
        {
            Endpoint = endpoint,
            ApiKey = apiKey,
            DefaultModel = string.IsNullOrWhiteSpace(model) ? "gpt-4o" : model
        });
    }

    return Results.Ok(new { message = "Settings saved successfully" });
});

app.MapPost("/api/settings/test", async (TestConnectionRequest request, SettingsTestService testService, UserSettingsService settingsService) =>
{
    // If the API key is masked, fill in the real stored value for testing
    var dto = request.Settings;
    if (!string.IsNullOrEmpty(dto.AzureAiApiKey) && dto.AzureAiApiKey.Contains("••••"))
    {
        var stored = settingsService.GetRealApiKey("AzureAI:ApiKey");
        if (stored != null) dto.AzureAiApiKey = stored;
    }
    if (!string.IsNullOrEmpty(dto.DataverseClientSecret) && dto.DataverseClientSecret.Contains("••••"))
    {
        var stored = settingsService.GetRealApiKey("Dataverse:ClientSecret");
        if (stored != null) dto.DataverseClientSecret = stored;
    }
    if (!string.IsNullOrEmpty(dto.GitHubToken) && dto.GitHubToken.Contains("••••"))
    {
        var stored = settingsService.GetRealApiKey("GitHub:Token");
        if (stored != null) dto.GitHubToken = stored;
    }

    var result = await testService.TestAsync(request.Service, dto);
    return Results.Ok(result);
});

app.Run();
