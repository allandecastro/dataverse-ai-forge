using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataverseForge.Infrastructure.Middleware;

/// <summary>
/// Shared-secret bearer-token gate for the <c>/api/*</c> surface. Closes the
/// "every endpoint is anonymous" P0 from <c>TODO.md</c>.
///
/// Opt-in: when <see cref="BearerTokenAuthOptions.SharedSecret"/> is null/empty
/// the middleware passes every request through unchanged. The default localhost
/// dev flow keeps working without any config change. Set the secret to require
/// <c>Authorization: Bearer &lt;secret&gt;</c> on requests under <c>/api/*</c>.
///
/// What is NOT gated, even when enabled:
/// <list type="bullet">
///   <item>Anything outside <c>/api/*</c> (assets, OpenAPI, <c>/healthz</c>, <c>/readyz</c>).</item>
///   <item>CORS preflight <c>OPTIONS</c> requests — browsers don't send an
///         <c>Authorization</c> header on preflight and the CORS middleware
///         upstream short-circuits these anyway.</item>
/// </list>
///
/// Wire it in <see cref="DependencyInjection.AddBearerTokenAuth"/> + the
/// pipeline registration in <c>Program.cs</c>. Place it AFTER <c>UseCors</c>
/// so preflight responses still carry the right CORS headers, but BEFORE
/// endpoint mapping so unauthenticated requests never reach the handler.
/// </summary>
public sealed class BearerTokenAuthMiddleware(
    RequestDelegate next,
    IOptionsMonitor<BearerTokenAuthOptions> options,
    ILogger<BearerTokenAuthMiddleware> logger)
{
    private const string BearerPrefix = "Bearer ";

    public async Task InvokeAsync(HttpContext context)
    {
        var secret = options.CurrentValue.SharedSecret;

        // Opt-in: no secret configured → middleware is a no-op. Critical for
        // the localhost dev loop; turning it into a hard requirement would
        // break every running setup the day this ships.
        if (string.IsNullOrWhiteSpace(secret))
        {
            await next(context);
            return;
        }

        // Only gate /api/* — health probes (/healthz, /readyz) and any future
        // public asset stay open.
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // CORS preflight bypass. The CORS middleware upstream normally
        // short-circuits OPTIONS, but if it ever stops doing so (config drift)
        // we don't want every browser preflight failing with 401.
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader)
            || authHeader.Count == 0
            || string.IsNullOrWhiteSpace(authHeader[0]))
        {
            await RejectAsync(context, "missing Authorization header");
            return;
        }

        var raw = authHeader[0]!;
        if (!raw.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(context, "Authorization is not Bearer scheme");
            return;
        }

        var presented = raw.AsSpan(BearerPrefix.Length).Trim().ToString();

        if (!ConstantTimeEquals(presented, secret!))
        {
            await RejectAsync(context, "bearer token mismatch");
            return;
        }

        await next(context);
    }

    private async Task RejectAsync(HttpContext context, string reason)
    {
        // Log the specific reason server-side; the client always sees the same
        // opaque body so probing can't distinguish "no header" from "bad token".
        logger.LogWarning(
            "BearerToken: rejecting {Method} {Path} — {Reason}",
            context.Request.Method,
            context.Request.Path,
            reason);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer realm=\"dataverse-forge\"";
        await context.Response.WriteAsync("Unauthorized");
    }

    /// <summary>
    /// XOR-folded compare so an attacker can't time the response to learn how
    /// many leading bytes of the token are correct. Length mismatch returns
    /// early — leaking length is the standard trade-off (also leaked by the
    /// header parsing above) and avoiding it would require padding bookkeeping
    /// we don't need at this threat model.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }
}
