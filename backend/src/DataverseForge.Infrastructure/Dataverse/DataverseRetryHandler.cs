using System.Net;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

/// <summary>
/// Retry handler for Dataverse API calls. Handles HTTP 429 (TooManyRequests) and 503
/// (ServiceUnavailable) with exponential backoff. Respects the Retry-After header.
/// Max 3 retries with 2^n second backoff.
/// </summary>
internal sealed class DataverseRetryHandler(ILogger<DataverseRetryHandler> logger) : DelegatingHandler
{
    private const int MaxRetries = 3;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = GetRetryDelay(response, attempt);
                logger.LogWarning("Dataverse API retry {Attempt}/{Max} after {Delay}s for {Method} {Url}",
                    attempt, MaxRetries, delay.TotalSeconds, request.Method, request.RequestUri);

                await Task.Delay(delay, cancellationToken);

                // Clone the request for retry (original may have been consumed)
                request = await CloneRequestAsync(request);
            }

            response = await base.SendAsync(request, cancellationToken);

            if (!IsRetryable(response.StatusCode))
            {
                return response;
            }

            if (attempt == MaxRetries)
            {
                logger.LogError("Dataverse API max retries ({Max}) exhausted for {Method} {Url} — status {StatusCode}",
                    MaxRetries, request.Method, request.RequestUri, (int)response.StatusCode);
            }
        }

        return response!;
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static TimeSpan GetRetryDelay(HttpResponseMessage? response, int attempt)
    {
        // Respect Retry-After header if present
        if (response?.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response?.Headers.RetryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        // Exponential backoff: 2^attempt seconds (2s, 4s, 8s)
        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content != null)
        {
            var content = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(content);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
