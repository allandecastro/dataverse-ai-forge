using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Middleware;

/// <summary>
/// Injects a correlation ID into every request. Uses the incoming X-Correlation-Id header
/// if present, otherwise generates a new one. Adds the ID to the response headers and
/// creates a logging scope so all log entries include the correlation ID.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        // Format-string scope (not a raw Dictionary): JsonConsoleFormatter emits
        // a "Message" property per scope = scope.ToString(). Dictionary.ToString()
        // dumps the type name into logs ("System.Collections.Generic.Dictionary`2[...]").
        // FormattedLogValues renders the template properly.
        using (logger.BeginScope("CID={CorrelationId}", correlationId))
        {
            await next(context);
        }
    }
}
