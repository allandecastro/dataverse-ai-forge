using Azure.Core;

namespace DataverseForge.Infrastructure.Dataverse;

/// <summary>
/// Singleton holder for the active Dataverse <see cref="TokenCredential"/> and
/// the most recent acquired access token. Shared across every request so the
/// credential / token survive the per-request <c>DataverseAuthHandler</c>
/// instances that <see cref="IHttpClientFactory"/> rotates.
///
/// <para>
/// Why a separate service: <c>DataverseAuthHandler</c> is registered transient
/// (recreated by the HttpClient pipeline). Keeping the cache on the handler
/// itself meant a fresh sign-in could be silently picked up by an old cached
/// instance, OR a credential hot-swap (Settings → Save) wouldn't take effect
/// until the next handler rotation. Centralizing here makes both predictable.
/// </para>
///
/// <para>
/// Also exposes <see cref="Reset"/> — used by the "Sign out" endpoint to drop
/// the cached <see cref="Azure.Identity.InteractiveBrowserCredential"/> token,
/// forcing the next call to prompt for sign-in again. Without this, switching
/// accounts in Interactive mode required restarting the backend.
/// </para>
/// </summary>
// Public so the Api project's Sign-Out endpoint can inject it. The Infrastructure
// project doesn't grant InternalsVisibleTo to Api (only to Tests).
public sealed class DataverseCredentialCache
{
    private readonly object _lock = new();
    private TokenCredential? _credential;
    private string? _credentialKey;
    private AccessToken _cachedToken;
    private string? _cachedTokenKey;

    public TokenCredential? GetCachedCredential(string key)
    {
        lock (_lock)
        {
            return _credentialKey == key ? _credential : null;
        }
    }

    public void SetCredential(string key, TokenCredential credential)
    {
        lock (_lock)
        {
            _credential = credential;
            _credentialKey = key;
        }
    }

    public AccessToken? GetCachedToken(string key)
    {
        lock (_lock)
        {
            // Two-minute leeway — refresh proactively before expiry so an in-flight
            // call doesn't get a token that expires mid-request.
            if (_cachedTokenKey == key && _cachedToken.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return _cachedToken;
            }
            return null;
        }
    }

    public void SetToken(string key, AccessToken token)
    {
        lock (_lock)
        {
            _cachedToken = token;
            _cachedTokenKey = key;
        }
    }

    /// <summary>
    /// Drops the cached credential and token. The next Dataverse call rebuilds
    /// the credential and (for Interactive mode) re-opens the browser sign-in.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _credential = null;
            _credentialKey = null;
            _cachedToken = default;
            _cachedTokenKey = null;
        }
    }
}
