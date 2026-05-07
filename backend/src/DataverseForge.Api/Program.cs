using DataverseForge.Api.Endpoints;
using DataverseForge.Infrastructure;
using DataverseForge.Infrastructure.Middleware;

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

// CORS — configurable via Cors:AllowedOrigins (comma-separated or string array).
// Falls back to http://localhost:3000 for local dev. Required for the staging /
// docker-compose deployment where the frontend lives on a different host.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000" };
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
builder.Services.AddDataverseClient(builder.Configuration);
builder.Services.AddAzureAiChatClients(builder.Configuration);
builder.Services.AddStores(builder.Configuration);
builder.Services.AddSessionContext();
builder.Services.AddXsdValidator();
builder.Services.AddSolutionArchitect();
builder.Services.AddBusinessAnalyst();
builder.Services.AddDataModelBuilder();
builder.Services.AddUIBuilder();
builder.Services.AddDataPopulation();
builder.Services.AddTester();
builder.Services.AddAgentOrchestrator();
builder.Services.AddAgUi();
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

// ─── ENDPOINT GROUPS ───
// Each group lives in Endpoints/*Endpoints.cs as an IEndpointRouteBuilder extension.
app.MapHealthEndpoints();
app.MapChatEndpoints();
app.MapAuditEndpoints();
app.MapSessionEndpoints();
app.MapSettingsEndpoints();
app.MapExportsEndpoints();

app.Run();

// Exposed so WebApplicationFactory<Program> in the Infrastructure.Tests
// integration suite can spin up the host. With top-level statements the
// implicit Program class is internal; this turns it public.
public partial class Program;
