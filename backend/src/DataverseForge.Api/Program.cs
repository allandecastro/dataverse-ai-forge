using System.Threading.RateLimiting;
using DataverseForge.Api.Endpoints;
using DataverseForge.Infrastructure;
using DataverseForge.Infrastructure.Middleware;
using DataverseForge.Infrastructure.Settings;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ─── USER SETTINGS (highest-priority, hot-reloadable, secrets encrypted at rest) ───
// Lives in the writable data dir — the SAME path UserSettingsService writes to
// (ForgePaths / F-44). Previously this read from AppContext.BaseDirectory while
// the service wrote to the data dir, so freshly-saved settings were never read
// back on reload; aligning the path fixes that.
//
// Secret values (AzureAI:ApiKey, Dataverse:ClientSecret, GitHub:Token) are
// AES-GCM encrypted on disk and decrypted transparently here, so all downstream
// options binding (IOptionsMonitor<DataverseOptions>, AzureAiOptions, …) is
// unchanged. Legacy plaintext files keep working until the next save re-encrypts.
var userSettingsPath = Path.Combine(ForgePaths.GetWritableDataDir(), "user-settings.json");
builder.Configuration.AddEncryptedJsonFile(userSettingsPath, AesGcmSecretProtector.ForDataDir());

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
// Rate limiting — guards the expensive /api/agui SSE endpoint against runaway
// or abusive request rates once the backend is exposed beyond localhost. The
// limit is generous (a single interactive user never approaches it) and tunable
// via RateLimiting:AgUiPermitsPerMinute. Rejected requests get 429.
const string AgUiRateLimitPolicy = "agui";
var aguiPermitsPerMinute = builder.Configuration.GetValue<int?>("RateLimiting:AgUiPermitsPerMinute") ?? 60;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(AgUiRateLimitPolicy, limiterOptions =>
    {
        limiterOptions.PermitLimit = aguiPermitsPerMinute;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0; // shed load immediately rather than buffering streams
    });
});

builder.Services.AddDataverseClient(builder.Configuration);
builder.Services.AddAzureAiChatClients(builder.Configuration);
builder.Services.AddStores(builder.Configuration);
builder.Services.AddSessionContext();
builder.Services.AddXsdValidator();
builder.Services.AddBearerTokenAuth(builder.Configuration);
builder.Services.AddSolutionArchitect();
builder.Services.AddBusinessAnalyst();
builder.Services.AddDataModelBuilder();
builder.Services.AddUIBuilder();
builder.Services.AddDataPopulation();
builder.Services.AddSecurityBuilder();
builder.Services.AddAutomationBuilder();
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
// HTTPS redirection is opt-in via Security:RequireHttps so localhost dev (the
// frontend proxies to http://localhost:5xxx) is unaffected. Operators flip this
// on — together with a configured HTTPS port — before exposing the backend.
if (app.Configuration.GetValue<bool>("Security:RequireHttps"))
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseCors();
app.UseRateLimiter();
// Bearer-token gate on /api/* — opt-in via Auth:SharedSecret config. No-op
// when unset, so this is safe to leave on permanently. AFTER UseCors so
// preflight OPTIONS still gets CORS headers; BEFORE endpoint mapping so
// unauthenticated requests don't reach handler code.
app.UseMiddleware<BearerTokenAuthMiddleware>();

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
