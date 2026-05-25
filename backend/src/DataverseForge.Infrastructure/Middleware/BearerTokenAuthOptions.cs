namespace DataverseForge.Infrastructure.Middleware;

/// <summary>
/// Configuration for <see cref="BearerTokenAuthMiddleware"/>. Bound from the
/// <c>Auth</c> section of configuration via <see cref="DependencyInjection.AddBearerTokenAuth"/>.
///
/// Opt-in by design: leaving <see cref="SharedSecret"/> null/empty disables
/// enforcement entirely, preserving the project's localhost-first dev default.
/// Set it in user-secrets (dev) or environment configuration (anywhere else)
/// to require <c>Authorization: Bearer &lt;secret&gt;</c> on <c>/api/*</c>.
/// </summary>
public sealed class BearerTokenAuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Shared secret. When null/empty/whitespace the middleware is a no-op.
    /// Persist via user-secrets or environment variables only — never commit
    /// a real value to <c>appsettings.json</c>.
    /// </summary>
    public string? SharedSecret { get; set; }
}
