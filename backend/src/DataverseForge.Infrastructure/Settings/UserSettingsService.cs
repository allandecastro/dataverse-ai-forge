using System.Text.Json;
using System.Text.Json.Nodes;
using DataverseForge.Infrastructure.AzureAI;
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
        : this(configuration, logger, Path.Combine(AppContext.BaseDirectory, "user-settings.json"))
    {
    }

    // Test seam — lets the test suite pin the settings file to a temp directory
    // without touching AppContext.BaseDirectory.
    internal UserSettingsService(IConfiguration configuration, ILogger<UserSettingsService> logger, string filePath)
    {
        _configuration = configuration;
        _logger = logger;
        _filePath = filePath;
    }

    public SettingsDto GetCurrentSettings()
    {
        return new SettingsDto
        {
            AzureAiEndpoint = _configuration["AzureAI:Endpoint"] ?? string.Empty,
            AzureAiApiKey = MaskSecret(_configuration["AzureAI:ApiKey"]),
            AzureAiDefaultModel = _configuration["AzureAI:DefaultModel"] ?? AzureAiOptions.DefaultModelFallback,

            DataverseEnvironmentUrl = _configuration["Dataverse:EnvironmentUrl"] ?? string.Empty,
            DataverseTenantId = _configuration["Dataverse:TenantId"] ?? string.Empty,
            DataverseClientId = _configuration["Dataverse:ClientId"] ?? string.Empty,
            DataverseClientSecret = MaskSecret(_configuration["Dataverse:ClientSecret"]),
            DataverseAuthMode = _configuration["Dataverse:AuthMode"] ?? "ServicePrincipal",

            GitHubToken = MaskSecret(_configuration["GitHub:Token"])
        };
    }

    public async Task SaveSettingsAsync(SettingsDto dto)
    {
        await _writeLock.WaitAsync();
        try
        {
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

            // Azure AI section
            var ai = GetOrCreateSection(root, "AzureAI");
            SetIfNotMasked(ai, "Endpoint", dto.AzureAiEndpoint);
            SetIfNotMasked(ai, "ApiKey", dto.AzureAiApiKey);
            SetIfNotMasked(ai, "DefaultModel", dto.AzureAiDefaultModel);

            // Dataverse section
            var dv = GetOrCreateSection(root, "Dataverse");
            SetIfNotMasked(dv, "EnvironmentUrl", dto.DataverseEnvironmentUrl);
            SetIfNotMasked(dv, "TenantId", dto.DataverseTenantId);
            SetIfNotMasked(dv, "ClientId", dto.DataverseClientId);
            SetIfNotMasked(dv, "ClientSecret", dto.DataverseClientSecret);
            // AuthMode is not a secret — write directly. Empty string falls back to
            // SetIfNotMasked's empty-skip; we want to persist explicit values only.
            if (!string.IsNullOrWhiteSpace(dto.DataverseAuthMode))
            {
                dv["AuthMode"] = dto.DataverseAuthMode;
            }

            // GitHub section
            var gh = GetOrCreateSection(root, "GitHub");
            SetIfNotMasked(gh, "Token", dto.GitHubToken);

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
        if (string.IsNullOrEmpty(value) || value.Contains("••••"))
            return;

        section[key] = value;
    }

    /// <summary>
    /// Returns the real (unmasked) value for a secret config key.
    /// Used when the frontend sends a masked value and we need the real one for testing.
    /// </summary>
    public string? GetRealApiKey(string configKey)
    {
        var value = _configuration[configKey];
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value;
    }

    private static string MaskSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (value.Length <= 4) return "••••";
        return value[..4] + "••••••••";
    }
}
