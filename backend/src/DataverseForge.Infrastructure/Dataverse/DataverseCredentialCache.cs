using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Azure.Core;
using DataverseForge.Application.Contracts;

namespace DataverseForge.Infrastructure.Dataverse;

/// <summary>
/// Credentials and access tokens, one entry per Dataverse connection. Projects target
/// different environments, so a single-slot cache would make two open projects keep
/// evicting each other's token (and, in Interactive mode, re-prompt the browser).
/// Singleton: survives the per-request auth handler, and lets the sign-out endpoints
/// drop an entry to force a fresh sign-in.
/// </summary>
// Public so the Api project's sign-out endpoints can inject it. The Infrastructure
// project doesn't grant InternalsVisibleTo to Api (only to Tests).
public sealed class DataverseCredentialCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    private sealed class Entry(TokenCredential credential)
    {
        public TokenCredential Credential { get; } = credential;
        public AccessToken? Token { get; set; }
    }

    /// <summary>
    /// Cache key for a connection. Includes the auth mode (flipping SP↔Interactive must
    /// rebuild the credential) and a hash of the secret, so a rotated secret takes effect
    /// immediately instead of after the cached token expires.
    /// </summary>
    public static string KeyFor(DataverseConnection c)
    {
        var secretHash = string.IsNullOrEmpty(c.ClientSecret)
            ? ""
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.ClientSecret)))[..16];
        return $"{c.AuthMode}|{c.TenantId}|{c.ClientId}|{c.EnvironmentUrl.TrimEnd('/')}|{secretHash}";
    }

    public TokenCredential GetOrAddCredential(string key, Func<TokenCredential> factory) =>
        _entries.GetOrAdd(key, _ => new Entry(factory())).Credential;

    public AccessToken? GetCachedToken(string key)
    {
        // Two-minute leeway — refresh proactively before expiry so an in-flight call
        // doesn't get a token that expires mid-request.
        if (_entries.TryGetValue(key, out var e) && e.Token is { } t && t.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
            return t;
        return null;
    }

    public void SetToken(string key, AccessToken token)
    {
        if (_entries.TryGetValue(key, out var e))
            e.Token = token;
    }

    /// <summary>Signs one connection out (its next call re-authenticates).</summary>
    public void Reset(string key) => _entries.TryRemove(key, out _);

    /// <summary>Signs every connection out.</summary>
    public void Reset() => _entries.Clear();
}
