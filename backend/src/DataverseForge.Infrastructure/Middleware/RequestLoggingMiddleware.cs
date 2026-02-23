using System.Diagnostics;
using DataverseForge.Application.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Middleware;

/// <summary>
/// Logs HTTP request start/end with method, path, status code, and duration.
/// Creates an OpenTelemetry span for each request.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path;
        var correlationId = context.Items.TryGetValue("CorrelationId", out var cid) ? cid?.ToString() : "unknown";

        logger.LogInformation("HTTP {Method} {Path} started [CID={CorrelationId}]",
            method, path, correlationId);

        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();
            var statusCode = context.Response.StatusCode;

            if (statusCode >= 400)
            {
                logger.LogWarning("HTTP {Method} {Path} completed {StatusCode} in {Duration}ms [CID={CorrelationId}]",
                    method, path, statusCode, sw.ElapsedMilliseconds, correlationId);
            }
            else
            {
                logger.LogInformation("HTTP {Method} {Path} completed {StatusCode} in {Duration}ms [CID={CorrelationId}]",
                    method, path, statusCode, sw.ElapsedMilliseconds, correlationId);
            }
        }
    }
}
