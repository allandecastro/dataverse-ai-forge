using System.Reflection;
using DataverseForge.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDataverseClient(builder.Configuration);

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

app.Run();
