using System.Reflection;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Application.Orchestration;
using DataverseForge.Infrastructure;
using DataverseForge.Infrastructure.AgUi;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddAzureOpenAiChatClient(builder.Configuration);
builder.Services.AddStores();
builder.Services.AddSessionContext();
builder.Services.AddSolutionArchitect();
builder.Services.AddBusinessAnalyst();
builder.Services.AddDataModelBuilder();
builder.Services.AddAgentOrchestrator();
builder.Services.AddAgUi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

app.MapGet("/healthz", () => new
{
    Status = "healthy",
    Timestamp = DateTime.UtcNow,
    Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"
});

app.MapPost("/api/chat", async (ChatRequest request, AgentOrchestrator orchestrator, CancellationToken ct) =>
{
    var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N");
    var response = await orchestrator.ChatAsync(sessionId, request.Message, ct);
    return Results.Ok(response);
});

app.MapPost("/api/copilotkit", async (HttpContext ctx, OrchestratingChatClient client, IProjectStateStore projectStateStore, CancellationToken ct) =>
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

    var stream = client.GetStreamingResponseAsync(messages, options, ct);
    await AgUiSseWriter.WriteStreamAsync(ctx.Response, threadId, runId, agentName, stream, ct);
});

app.Run();
