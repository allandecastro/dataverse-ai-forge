using System.Reflection;
using DataverseForge.Application.Dtos;
using DataverseForge.Application.Orchestration;
using DataverseForge.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDataverseClient(builder.Configuration);
builder.Services.AddAzureOpenAiChatClient(builder.Configuration);
builder.Services.AddStores();
builder.Services.AddSessionContext();
builder.Services.AddSolutionArchitect();
builder.Services.AddBusinessAnalyst();
builder.Services.AddDataModelBuilder();
builder.Services.AddAgentOrchestrator();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

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

app.Run();
