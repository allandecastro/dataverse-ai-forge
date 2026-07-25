using DataverseForge.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;

namespace DataverseForge.Infrastructure.Tests.Settings;

public class SecretProtectionTests
{
    private static byte[] Key(byte seed) => Enumerable.Range(0, 32).Select(i => (byte)(i + seed)).ToArray();

    private static AesGcmSecretProtector Protector(byte seed = 0) => new(Key(seed));

    // ─── AesGcmSecretProtector ───

    [Theory]
    [InlineData("sk-abcdef0123456789")]
    [InlineData("a")]
    [InlineData("unicode-secret-é-🔐-value")]
    [InlineData("{\"nested\":\"json secret\"}")]
    public void ProtectThenUnprotect_RoundTrips(string plaintext)
    {
        var p = Protector();
        var protectedValue = p.Protect(plaintext);

        Assert.NotEqual(plaintext, protectedValue);
        Assert.StartsWith("enc:v1:", protectedValue);
        // (The "plaintext not present verbatim" check lives in the realistic-secret
        // tests below / in UserSettingsServiceTests — a 1-char value like "a"
        // inevitably appears in the base64 alphabet, which is a false positive here.)
        Assert.Equal(plaintext, p.Unprotect(protectedValue));
    }

    [Fact]
    public void Protect_IsNonDeterministic_ButBothDecrypt()
    {
        // Random nonce per call → two encryptions of the same value differ,
        // which prevents an attacker from spotting reused secrets on disk.
        var p = Protector();
        var a = p.Protect("same-secret");
        var b = p.Protect("same-secret");

        Assert.NotEqual(a, b);
        Assert.Equal("same-secret", p.Unprotect(a));
        Assert.Equal("same-secret", p.Unprotect(b));
    }

    [Fact]
    public void Protect_IsIdempotent_OnAlreadyProtectedAndEmpty()
    {
        var p = Protector();
        var once = p.Protect("secret");
        Assert.Equal(once, p.Protect(once));   // does not double-encrypt
        Assert.Equal("", p.Protect(""));
    }

    [Fact]
    public void Unprotect_PassesThroughLegacyPlaintext()
    {
        // Pre-migration files hold raw secrets with no prefix — must round-trip
        // unchanged so existing installs keep working until the next save.
        var p = Protector();
        Assert.Equal("legacy-plaintext-key", p.Unprotect("legacy-plaintext-key"));
        Assert.Equal("", p.Unprotect(""));
    }

    [Fact]
    public void Unprotect_WithWrongKey_DegradesToEmpty()
    {
        var enc = Protector(seed: 0).Protect("secret");
        // Different key (rotated / regenerated .secret.key) can't authenticate.
        Assert.Equal("", Protector(seed: 7).Unprotect(enc));
    }

    [Theory]
    [InlineData("enc:v1:not-valid-base64!!!")]
    [InlineData("enc:v1:dG9vc2hvcnQ=")] // valid base64 but too short for nonce+tag
    public void Unprotect_WithCorruptCiphertext_DegradesToEmpty(string corrupt)
    {
        Assert.Equal("", Protector().Unprotect(corrupt));
    }

    [Fact]
    public void Constructor_RejectsWrongKeyLength()
    {
        Assert.Throws<ArgumentException>(() => new AesGcmSecretProtector(new byte[16]));
    }

    // ─── EncryptedJsonConfigurationSource (read path) ───

    [Fact]
    public void EncryptedJsonFile_DecryptsSecretsAndPassesThroughTheRest()
    {
        var p = Protector();
        var path = Path.Combine(Path.GetTempPath(), $"forge-enc-{Guid.NewGuid():N}.json");
        try
        {
            var encApiKey = p.Protect("real-api-key");
            File.WriteAllText(path, $$"""
            {
              "AzureAI": { "ApiKey": "{{encApiKey}}", "Endpoint": "https://x" },
              "Dataverse": { "ClientSecret": "legacy-plaintext-secret", "ClientId": "client-123" }
            }
            """);

            var config = new ConfigurationBuilder().AddEncryptedJsonFile(path, p).Build();

            // An encrypted secret comes back decrypted...
            Assert.Equal("real-api-key", config["AzureAI:ApiKey"]);
            // ...a legacy PLAINTEXT secret (pre-encryption files) passes through unchanged...
            Assert.Equal("legacy-plaintext-secret", config["Dataverse:ClientSecret"]);
            // ...and non-secret fields are untouched.
            Assert.Equal("https://x", config["AzureAI:Endpoint"]);
            Assert.Equal("client-123", config["Dataverse:ClientId"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void EncryptedJsonFile_MissingFile_IsOptional()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-missing-{Guid.NewGuid():N}.json");
        var config = new ConfigurationBuilder().AddEncryptedJsonFile(path, Protector()).Build();
        Assert.Null(config["Dataverse:ClientSecret"]);
    }
}
