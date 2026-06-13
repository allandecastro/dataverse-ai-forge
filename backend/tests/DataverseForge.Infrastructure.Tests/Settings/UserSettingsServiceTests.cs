using System.Text.Json;
using DataverseForge.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataverseForge.Infrastructure.Tests.Settings;

public class UserSettingsServiceTests : IDisposable
{
    private readonly string _tempPath;

    public UserSettingsServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"forge-settings-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    private static IConfiguration ConfigFrom(IDictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? new Dictionary<string, string?>()).Build();

    // Fixed key so the test can decrypt what the service writes and assert the
    // round-trip. A constant key keeps these tests deterministic and off the
    // real .secret.key file.
    private static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private readonly ISecretProtector _protector = new AesGcmSecretProtector(TestKey);

    private UserSettingsService Make(IConfiguration config) =>
        new(config, NullLogger<UserSettingsService>.Instance, _protector, _tempPath);

    // ─── GetCurrentSettings — masking ───

    [Fact]
    public void GetCurrentSettings_MasksAllSecretFields()
    {
        var config = ConfigFrom(new Dictionary<string, string?>
        {
            ["AzureAI:Endpoint"] = "https://contoso.openai.azure.com/",
            ["AzureAI:ApiKey"] = "sk-abcdef0123456789",
            ["AzureAI:DefaultModel"] = "gpt-4o",
            ["Dataverse:EnvironmentUrl"] = "https://contoso.crm.dynamics.com",
            ["Dataverse:TenantId"] = "tenant-123",
            ["Dataverse:ClientId"] = "client-456",
            ["Dataverse:ClientSecret"] = "verysecret-abcdef0123",
            ["GitHub:Token"] = "ghp_xxxxxxxxxxxxxxxx",
        });

        var settings = Make(config).GetCurrentSettings();

        // Endpoint, model, tenant, client id are NOT secrets — pass through.
        Assert.Equal("https://contoso.openai.azure.com/", settings.AzureAiEndpoint);
        Assert.Equal("gpt-4o", settings.AzureAiDefaultModel);
        Assert.Equal("tenant-123", settings.DataverseTenantId);
        Assert.Equal("client-456", settings.DataverseClientId);

        // Secrets are masked: first 4 chars + bullets.
        Assert.StartsWith("sk-a", settings.AzureAiApiKey);
        Assert.Contains("••••", settings.AzureAiApiKey);
        Assert.DoesNotContain("0123456789", settings.AzureAiApiKey);

        Assert.StartsWith("very", settings.DataverseClientSecret);
        Assert.Contains("••••", settings.DataverseClientSecret);

        Assert.StartsWith("ghp_", settings.GitHubToken);
        Assert.Contains("••••", settings.GitHubToken);
    }

    [Fact]
    public void GetCurrentSettings_ShortSecretsAreFullyMasked()
    {
        var settings = Make(ConfigFrom(new Dictionary<string, string?>
        {
            ["AzureAI:ApiKey"] = "abc",  // <= 4 chars
        })).GetCurrentSettings();

        Assert.Equal("••••", settings.AzureAiApiKey);
    }

    [Fact]
    public void GetCurrentSettings_EmptySecretsReturnEmptyString()
    {
        var settings = Make(ConfigFrom()).GetCurrentSettings();

        Assert.Equal(string.Empty, settings.AzureAiApiKey);
        Assert.Equal(string.Empty, settings.DataverseClientSecret);
        Assert.Equal(string.Empty, settings.GitHubToken);
    }

    [Fact]
    public void GetCurrentSettings_AppliesDefaultsForMissingNonSecrets()
    {
        var settings = Make(ConfigFrom()).GetCurrentSettings();

        Assert.Equal("gpt-4o", settings.AzureAiDefaultModel);
    }

    // ─── GetRealApiKey — unmasked passthrough ───

    [Fact]
    public void GetRealApiKey_ReturnsUnmaskedValue()
    {
        var svc = Make(ConfigFrom(new Dictionary<string, string?>
        {
            ["AzureAI:ApiKey"] = "sk-real-key-abc123",
        }));

        Assert.Equal("sk-real-key-abc123", svc.GetRealApiKey("AzureAI:ApiKey"));
    }

    [Fact]
    public void GetRealApiKey_ReturnsNullForMissingKey()
    {
        var svc = Make(ConfigFrom());
        Assert.Null(svc.GetRealApiKey("AzureAI:ApiKey"));
    }

    // ─── SaveSettingsAsync — file I/O ───

    [Fact]
    public async Task SaveSettingsAsync_WritesNewFileWithExpectedShape()
    {
        var svc = Make(ConfigFrom());

        await svc.SaveSettingsAsync(new SettingsDto
        {
            AzureAiEndpoint = "https://contoso.openai.azure.com/",
            AzureAiApiKey = "real-key",
            AzureAiDefaultModel = "gpt-4o",
            DataverseEnvironmentUrl = "https://contoso.crm.dynamics.com",
            DataverseClientSecret = "real-secret",
        });

        Assert.True(File.Exists(_tempPath));
        var json = JsonDocument.Parse(await File.ReadAllTextAsync(_tempPath));

        // Non-secret fields are written verbatim.
        Assert.Equal("https://contoso.openai.azure.com/", json.RootElement.GetProperty("AzureAI").GetProperty("Endpoint").GetString());

        // Secrets are NOT on disk in cleartext — they're AES-GCM encrypted with
        // the "enc:v1:" prefix, and round-trip back to the original plaintext.
        var storedApiKey = json.RootElement.GetProperty("AzureAI").GetProperty("ApiKey").GetString()!;
        var storedSecret = json.RootElement.GetProperty("Dataverse").GetProperty("ClientSecret").GetString()!;

        Assert.StartsWith("enc:v1:", storedApiKey);
        Assert.DoesNotContain("real-key", storedApiKey);
        Assert.Equal("real-key", _protector.Unprotect(storedApiKey));

        Assert.StartsWith("enc:v1:", storedSecret);
        Assert.DoesNotContain("real-secret", storedSecret);
        Assert.Equal("real-secret", _protector.Unprotect(storedSecret));
    }

    [Fact]
    public async Task SaveSettingsAsync_DoesNotOverwriteWhenValueIsMasked()
    {
        // Symptom this protects: the user opens Settings, edits the endpoint, hits Save.
        // The form re-sends the masked ApiKey ("sk-a••••••••"). Without this guard, the
        // masked value would replace the real stored key.
        var svc = Make(ConfigFrom());
        await File.WriteAllTextAsync(_tempPath, """
            {
              "AzureAI": { "ApiKey": "real-stored-key" }
            }
            """);

        await svc.SaveSettingsAsync(new SettingsDto
        {
            AzureAiApiKey = "sk-a••••••••",  // masked re-submission
            AzureAiEndpoint = "https://updated.openai.azure.com/",
        });

        var json = JsonDocument.Parse(await File.ReadAllTextAsync(_tempPath));
        Assert.Equal("real-stored-key", json.RootElement.GetProperty("AzureAI").GetProperty("ApiKey").GetString());
        Assert.Equal("https://updated.openai.azure.com/", json.RootElement.GetProperty("AzureAI").GetProperty("Endpoint").GetString());
    }

    [Fact]
    public async Task SaveSettingsAsync_DoesNotOverwriteWhenValueIsEmpty()
    {
        var svc = Make(ConfigFrom());
        await File.WriteAllTextAsync(_tempPath, """{ "GitHub": { "Token": "preserved" } }""");

        await svc.SaveSettingsAsync(new SettingsDto
        {
            // GitHubToken is empty string by default — should NOT clobber existing value.
            AzureAiEndpoint = "anything",
        });

        var json = JsonDocument.Parse(await File.ReadAllTextAsync(_tempPath));
        Assert.Equal("preserved", json.RootElement.GetProperty("GitHub").GetProperty("Token").GetString());
    }

    [Fact]
    public async Task SaveSettingsAsync_PreservesUnknownTopLevelKeys()
    {
        // If a user / external tool added a custom section to user-settings.json, save
        // should leave it alone rather than clobber the file structure.
        var svc = Make(ConfigFrom());
        await File.WriteAllTextAsync(_tempPath, """
            {
              "CustomSection": { "MySetting": "preserve-me" }
            }
            """);

        await svc.SaveSettingsAsync(new SettingsDto { AzureAiEndpoint = "https://x" });

        var json = JsonDocument.Parse(await File.ReadAllTextAsync(_tempPath));
        Assert.Equal(
            "preserve-me",
            json.RootElement.GetProperty("CustomSection").GetProperty("MySetting").GetString());
    }
}
