using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Settings;

public sealed class UserSettingsService
{
    private readonly IConfiguration _configuration;
    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ILogger<UserSettingsService> _logger;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public UserSettingsService(IConfiguration configuration, ILogger<UserSettingsService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _filePath = Path.Combine(AppContext.BaseDirectory, "user-settings.json");
    }

    public SettingsDto GetCurrentSettings()
    {
        return new SettingsDto
        {
            AzureOpenAiEndpoint = _configuration["AzureOpenAI:Endpoint"] ?? string.Empty,
            AzureOpenAiApiKey = MaskSecret(_configuration["AzureOpenAI:ApiKey"]),
            AzureOpenAiGpt4oDeploymentName = _configuration["AzureOpenAI:Gpt4oDeploymentName"] ?? "gpt-4o",
            AzureOpenAiGpt4oMiniDeploymentName = _configuration["AzureOpenAI:Gpt4oMiniDeploymentName"] ?? "gpt-4o-mini",

            DataverseEnvironmentUrl = _configuration["Dataverse:EnvironmentUrl"] ?? string.Empty,
            DataverseTenantId = _configuration["Dataverse:TenantId"] ?? string.Empty,
            DataverseClientId = _configuration["Dataverse:ClientId"] ?? string.Empty,
            DataverseClientSecret = MaskSecret(_configuration["Dataverse:ClientSecret"]),

            GitHubToken = MaskSecret(_configuration["GitHub:Token"]),

            PersistenceProvider = _configuration["Persistence:Provider"] ?? "InMemory",
            PersistenceSqliteConnectionString = _configuration["Persistence:SqliteConnectionString"] ?? "Data Source=dataverse-forge.db",

            OpenTelemetryOtlpEndpoint = _configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317"
        };
    }

    public async Task SaveSettingsAsync(SettingsDto dto)
    {
        await _writeLock.WaitAsync();
        try
        {
            // Read existing file to preserve values not being overwritten
            JsonObject root;
            if (File.Exists(_filePath))
            {
                var existing = await File.ReadAllTextAsync(_filePath);
                root = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            // Azure OpenAI section
            var aoai = GetOrCreateSection(root, "AzureOpenAI");
            SetIfNotMasked(aoai, "Endpoint", dto.AzureOpenAiEndpoint);
            SetIfNotMasked(aoai, "ApiKey", dto.AzureOpenAiApiKey);
            SetIfNotMasked(aoai, "Gpt4oDeploymentName", dto.AzureOpenAiGpt4oDeploymentName);
            SetIfNotMasked(aoai, "Gpt4oMiniDeploymentName", dto.AzureOpenAiGpt4oMiniDeploymentName);

            // Dataverse section
            var dv = GetOrCreateSection(root, "Dataverse");
            SetIfNotMasked(dv, "EnvironmentUrl", dto.DataverseEnvironmentUrl);
            SetIfNotMasked(dv, "TenantId", dto.DataverseTenantId);
            SetIfNotMasked(dv, "ClientId", dto.DataverseClientId);
            SetIfNotMasked(dv, "ClientSecret", dto.DataverseClientSecret);

            // GitHub section
            var gh = GetOrCreateSection(root, "GitHub");
            SetIfNotMasked(gh, "Token", dto.GitHubToken);

            // Persistence section
            var persistence = GetOrCreateSection(root, "Persistence");
            SetIfNotMasked(persistence, "Provider", dto.PersistenceProvider);
            SetIfNotMasked(persistence, "SqliteConnectionString", dto.PersistenceSqliteConnectionString);

            // OpenTelemetry section
            var otel = GetOrCreateSection(root, "OpenTelemetry");
            SetIfNotMasked(otel, "OtlpEndpoint", dto.OpenTelemetryOtlpEndpoint);

            var json = root.ToJsonString(WriteOptions);
            await File.WriteAllTextAsync(_filePath, json);

            _logger.LogInformation("User settings saved to {FilePath}", _filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static JsonObject GetOrCreateSection(JsonObject root, string key)
    {
        if (root[key] is JsonObject existing)
            return existing;

        var section = new JsonObject();
        root[key] = section;
        return section;
    }

    private static void SetIfNotMasked(JsonObject section, string key, string value)
    {
        // Don't overwrite real values with masked placeholders
        if (string.IsNullOrEmpty(value) || value.Contains("••••"))
            return;

        section[key] = value;
    }

    private static string MaskSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (value.Length <= 4) return "••••";
        return value[..4] + "••••••••";
    }
}
