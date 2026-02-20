using System.Reflection;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Dtos;
using DataverseForge.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDataverseClient(builder.Configuration);
builder.Services.AddAzureOpenAiChatClient(builder.Configuration);
builder.Services.AddStores();
builder.Services.AddSolutionArchitect();

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

app.MapPost("/api/chat", async (ChatRequest request, SolutionArchitectAgent agent, CancellationToken ct) =>
{
    var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N");
    var response = await agent.ChatAsync(sessionId, request.Message, ct);
    return Results.Ok(response);
});

app.Run();
