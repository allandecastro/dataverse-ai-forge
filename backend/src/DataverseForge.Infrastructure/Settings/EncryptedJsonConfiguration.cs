using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace DataverseForge.Infrastructure.Settings;

/// <summary>
/// A JSON configuration source identical to <c>AddJsonFile</c> except that the
/// known secret keys are decrypted with an <see cref="ISecretProtector"/> as
/// they load. This keeps every downstream consumer
/// (<c>IOptionsMonitor&lt;DataverseOptions&gt;</c>, <c>AzureAiOptions</c>, …)
/// unchanged — they read plaintext from <see cref="IConfiguration"/> exactly as
/// before; only the bytes ON DISK are encrypted.
/// </summary>
public sealed class EncryptedJsonConfigurationSource : JsonConfigurationSource
{
    public required ISecretProtector Protector { get; init; }

    /// <summary>Colon-delimited config paths whose value is decrypted on load.</summary>
    public static readonly string[] SecretKeys =
    [
        "AzureAI:ApiKey",
        "Dataverse:ClientSecret",
        "GitHub:Token",
    ];

    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        EnsureDefaults(builder);
        return new EncryptedJsonConfigurationProvider(this);
    }
}

public sealed class EncryptedJsonConfigurationProvider(EncryptedJsonConfigurationSource source)
    : JsonConfigurationProvider(source)
{
    // Called by FileConfigurationProvider on initial load AND on every
    // reload-on-change, so decryption stays in sync with edits/Reload().
    public override void Load(Stream stream)
    {
        base.Load(stream);

        var protector = ((EncryptedJsonConfigurationSource)Source).Protector;
        foreach (var key in EncryptedJsonConfigurationSource.SecretKeys)
        {
            // Data uses a case-insensitive comparer, matching config key semantics.
            if (Data.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                Data[key] = protector.Unprotect(value);
            }
        }
    }
}

public static class EncryptedJsonConfigurationExtensions
{
    /// <summary>
    /// Adds <paramref name="path"/> as an optional, reload-on-change JSON config
    /// source whose secret keys are decrypted via <paramref name="protector"/>.
    /// </summary>
    public static IConfigurationBuilder AddEncryptedJsonFile(
        this IConfigurationBuilder builder, string path, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(protector);

        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath)!;

        var source = new EncryptedJsonConfigurationSource
        {
            Protector = protector,
            // Absolute paths need an explicit physical provider rooted at the
            // directory (PhysicalFileProvider rejects rooted relative paths).
            FileProvider = new PhysicalFileProvider(dir),
            Path = Path.GetFileName(fullPath),
            Optional = true,
            ReloadOnChange = true,
        };
        source.ResolveFileProvider();
        builder.Add(source);
        return builder;
    }
}
