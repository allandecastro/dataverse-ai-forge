using System.Text.Json;
using System.Text.Json.Nodes;
using DataverseForge.Application.Contracts;
using DataverseForge.Infrastructure.AzureAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Settings;

public sealed class UserSettingsService
{
    private readonly IConfiguration _configuration;
    private readonly string _filePath;
    private readonly ISecretProtector _protector;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ILogger<UserSettingsService> _logger;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public UserSettingsService(IConfiguration configuration, ILogger<UserSettingsService> logger, ISecretProtector protector)
        : this(configuration, logger, protector, Path.Combine(ForgePaths.GetWritableDataDir(), "user-settings.json"))
    {
        // F-44: route through ForgePaths so saved Dataverse credentials live
        // in the writable data dir (mountable via DVFORGE_DATA_DIR), not next
        // to the binary where they get wiped on container rebuild.
    }

    // Test seam — lets the test suite pin the settings file to a temp directory
    // without touching AppContext.BaseDirectory.
    internal UserSettingsService(IConfiguration configuration, ILogger<UserSettingsService> logger, ISecretProtector protector, string filePath)
    {
        _configuration = configuration;
        _logger = logger;
        _protector = protector;
        _filePath = filePath;
    }

    public SettingsDto GetCurrentSettings()
    {
        return new SettingsDto
        {
            AzureAiEndpoint = _configuration["AzureAI:Endpoint"] ?? string.Empty,
            AzureAiApiKey = MaskSecret(_configuration["AzureAI:ApiKey"]),
            AzureAiDefaultModel = _configuration["AzureAI:DefaultModel"] ?? AzureAiOptions.DefaultModelFallback,
            AzureAiAgentModels = _configuration.GetSection("AzureAI:AgentModels").Get<Dictionary<string, string>>() ?? new(),

            DataverseEnvironmentUrl = _configuration["Dataverse:EnvironmentUrl"] ?? string.Empty,
            DataverseTenantId = _configuration["Dataverse:TenantId"] ?? string.Empty,
            DataverseClientId = _configuration["Dataverse:ClientId"] ?? string.Empty,
            DataverseClientSecret = MaskSecret(_configuration["Dataverse:ClientSecret"]),
            DataverseAuthMode = _configuration["Dataverse:AuthMode"] ?? "ServicePrincipal",

            AzureDevOpsOrganizationUrl = _configuration["AzureDevOps:OrganizationUrl"] ?? string.Empty,
            AzureDevOpsProject = _configuration["AzureDevOps:Project"] ?? string.Empty,
            AzureDevOpsPersonalAccessToken = MaskSecret(_configuration["AzureDevOps:PersonalAccessToken"]),

            RagEmbeddingProvider = _configuration["Rag:EmbeddingProvider"] ?? "AzureOpenAI",
            RagEmbeddingDeployment = _configuration["Rag:EmbeddingDeployment"] ?? "text-embedding-3-small",
            GitHubPersonalAccessToken = MaskSecret(_configuration["Rag:GitHubPersonalAccessToken"]),
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

            // Azure AI section. ApiKey is a secret — encrypted at rest.
            var ai = GetOrCreateSection(root, "AzureAI");
            SetIfNotMasked(ai, "Endpoint", dto.AzureAiEndpoint);
            SetSecretIfNotMasked(ai, "ApiKey", dto.AzureAiApiKey);
            SetIfNotMasked(ai, "DefaultModel", dto.AzureAiDefaultModel);

            // Per-agent model overrides. Only touch the section when the client
            // actually sent the field (null = omitted → leave existing intact).
            // When present, rewrite the whole map so clearing an override in the
            // UI removes it; empty/blank values mean "inherit DefaultModel".
            if (dto.AzureAiAgentModels is not null)
            {
                var overrides = new JsonObject();
                foreach (var (agent, model) in dto.AzureAiAgentModels)
                {
                    if (!string.IsNullOrWhiteSpace(agent) && !string.IsNullOrWhiteSpace(model))
                        overrides[agent] = model.Trim();
                }
                ai["AgentModels"] = overrides;
            }

            // Dataverse and Azure DevOps are NOT global settings any more: each project
            // has its own connection (IProjectConnectionStore). Those DTO fields are only
            // used to test a connection, never written here.

            // RAG section. GitHub PAT is a secret — encrypted at rest.
            var rag = GetOrCreateSection(root, "Rag");
            SetIfNotMasked(rag, "EmbeddingProvider", dto.RagEmbeddingProvider);
            SetIfNotMasked(rag, "EmbeddingDeployment", dto.RagEmbeddingDeployment);
            SetSecretIfNotMasked(rag, "GitHubPersonalAccessToken", dto.GitHubPersonalAccessToken);

            var json = root.ToJsonString(WriteOptions);
            await File.WriteAllTextAsync(_filePath, json);

            _logger.LogInformation("User settings saved to {FilePath}", _filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// The pre-per-project global Dataverse / Azure DevOps connection still stored in
    /// user-settings.json, secrets decrypted — or null when there is none. Read from the
    /// file only (not the merged configuration), so appsettings placeholders and env vars
    /// never count as a user's real connection.
    /// </summary>
    public async Task<ProjectConnection?> ReadLegacyConnectionAsync()
    {
        if (!File.Exists(_filePath)) return null;
        var root = JsonNode.Parse(await File.ReadAllTextAsync(_filePath))?.AsObject();
        if (root is null) return null;

        string Str(JsonObject? section, string key) => section?[key]?.GetValue<string>() ?? string.Empty;

        var dvSection = root["Dataverse"] as JsonObject;
        var dataverse = new DataverseConnection(
            Str(dvSection, "EnvironmentUrl"),
            Str(dvSection, "AuthMode") is { Length: > 0 } mode ? mode : "ServicePrincipal",
            Str(dvSection, "TenantId"),
            Str(dvSection, "ClientId"),
            _protector.Unprotect(Str(dvSection, "ClientSecret")));

        var azdoSection = root["AzureDevOps"] as JsonObject;
        var azureDevOps = new AzureDevOpsConnection(
            Str(azdoSection, "OrganizationUrl"),
            Str(azdoSection, "Project"),
            _protector.Unprotect(Str(azdoSection, "PersonalAccessToken")));

        var hasDataverse = dataverse.IsConfigured && !dataverse.EnvironmentUrl.Contains("YOUR-ORG", StringComparison.OrdinalIgnoreCase);
        var hasAzureDevOps = azureDevOps.IsConfigured;
        if (!hasDataverse && !hasAzureDevOps) return null;

        return new ProjectConnection(hasDataverse ? dataverse : null, hasAzureDevOps ? azureDevOps : null);
    }

    /// <summary>Drops the legacy global Dataverse / Azure DevOps sections once migrated.</summary>
    public async Task RemoveLegacyConnectionAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            if (!File.Exists(_filePath)) return;
            var root = JsonNode.Parse(await File.ReadAllTextAsync(_filePath))?.AsObject();
            if (root is null) return;
            root.Remove("Dataverse");
            root.Remove("AzureDevOps");
            await File.WriteAllTextAsync(_filePath, root.ToJsonString(WriteOptions));
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
    /// Like <see cref="SetIfNotMasked"/> but encrypts the value at rest. The
    /// masked-skip guard means a re-submitted "••••" value leaves the existing
    /// (already-encrypted) value untouched — we never double-encrypt or clobber.
    /// </summary>
    private void SetSecretIfNotMasked(JsonObject section, string key, string value)
    {
        if (string.IsNullOrEmpty(value) || value.Contains("••••"))
            return;

        section[key] = _protector.Protect(value);
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
