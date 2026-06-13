using System.Security.Cryptography;
using System.Text;

namespace DataverseForge.Infrastructure.Settings;

/// <summary>
/// Encrypts/decrypts the secret values stored in user-settings.json so
/// credentials (Azure AI key, Dataverse client secret, GitHub token) are not
/// written to disk in cleartext (P0 hardening).
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Returns the encrypted, prefix-tagged form of <paramref name="plaintext"/>.
    /// Empty input and already-protected input are returned unchanged so the
    /// call is idempotent.
    /// </summary>
    string Protect(string plaintext);

    /// <summary>
    /// Reverses <see cref="Protect"/>. Values WITHOUT the protected prefix are
    /// returned verbatim (legacy plaintext / non-secret fields). Undecryptable
    /// protected values (rotated/missing key, tampering) degrade to an empty
    /// string so the app starts and the user is prompted to re-enter creds,
    /// rather than crashing config load at startup.
    /// </summary>
    string Unprotect(string stored);
}

/// <summary>
/// AES-256-GCM implementation of <see cref="ISecretProtector"/>.
///
/// Stored form: <c>enc:v1:</c> + base64(nonce(12) ‖ tag(16) ‖ ciphertext). The
/// 256-bit key is generated once and persisted via <see cref="SecretKeyFile"/>
/// with locked-down permissions.
///
/// Threat model (matches the TODO's "DPAPI / scoped data protection" bar): an
/// attacker who can read user-settings.json no longer recovers the raw secret.
/// An attacker who ALSO reads the key file can still decrypt — the key file's
/// restricted permissions and separate location are the mitigation, the same
/// tradeoff ASP.NET Core Data Protection makes with its key ring. This is a
/// local-/single-host tool; KMS-backed protection is out of scope.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    internal const string Prefix = "enc:v1:";
    private const int NonceSize = 12; // AES-GCM standard nonce length
    private const int TagSize = 16;   // 128-bit authentication tag
    internal const int KeySize = 32;  // AES-256

    private readonly byte[] _key;

    public AesGcmSecretProtector(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be exactly {KeySize} bytes (AES-256).", nameof(key));
        _key = key;
    }

    /// <summary>
    /// Default instance keyed off the <c>.secret.key</c> file in the writable
    /// data dir (the same dir user-settings.json lives in). Creates the key on
    /// first use.
    /// </summary>
    public static AesGcmSecretProtector ForDataDir() =>
        new(SecretKeyFile.LoadOrCreate(Path.Combine(ForgePaths.GetWritableDataDir(), ".secret.key")));

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        if (plaintext.StartsWith(Prefix, StringComparison.Ordinal)) return plaintext; // already protected

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    public string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored; // legacy plaintext / non-secret

        try
        {
            var blob = Convert.FromBase64String(stored[Prefix.Length..]);
            if (blob.Length < NonceSize + TagSize) return string.Empty;

            var nonce = blob.AsSpan(0, NonceSize);
            var tag = blob.AsSpan(NonceSize, TagSize);
            var cipher = blob.AsSpan(NonceSize + TagSize);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            // Corrupt base64, truncated blob, wrong key, or tampering — treat as
            // "no usable secret" so startup succeeds and the user re-enters it.
            return string.Empty;
        }
    }
}

/// <summary>
/// Loads (or creates on first use) the 256-bit key backing
/// <see cref="AesGcmSecretProtector"/>, persisted as base64 in a key file with
/// owner-only permissions on Unix.
/// </summary>
internal static class SecretKeyFile
{
    private static readonly object Gate = new();

    public static byte[] LoadOrCreate(string keyPath)
    {
        lock (Gate)
        {
            if (File.Exists(keyPath))
            {
                try
                {
                    var existing = Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
                    if (existing.Length == AesGcmSecretProtector.KeySize) return existing;
                }
                catch (FormatException)
                {
                    // Corrupt key file — fall through and regenerate. Existing
                    // ciphertext becomes undecryptable (Unprotect → empty), so the
                    // user re-enters credentials. Better than failing to start.
                }
            }

            var key = RandomNumberGenerator.GetBytes(AesGcmSecretProtector.KeySize);
            var dir = Path.GetDirectoryName(keyPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(keyPath, Convert.ToBase64String(key));
            TryRestrictPermissions(keyPath);
            return key;
        }
    }

    private static void TryRestrictPermissions(string keyPath)
    {
        if (OperatingSystem.IsWindows()) return; // NTFS inherits the user-profile ACL

        try
        {
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best-effort — restricted/unsupported filesystem. The key file still
            // lives in the data dir, not in user-settings.json next to ciphertext.
        }
    }
}
