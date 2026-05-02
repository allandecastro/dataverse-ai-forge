using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataverseForge.Infrastructure.Dataverse;

/// <summary>
/// Logs every Dataverse HTTP call's request body, response body, status, and
/// duration at Information level — but ONLY when <c>Dataverse:VerboseLogging</c>
/// is true. With the flag off, the handler is a pass-through (zero overhead).
///
/// Lives at the inner end of the HttpClient pipeline so it sees the URL after
/// <see cref="DataverseAuthHandler"/> rewrites it from the placeholder, plus
/// the bearer token already attached. The auth header is NOT logged — we strip
/// it from the headers we emit so log files don't leak credentials.
///
/// The HttpContent recreate dance after each read is necessary because reading
/// a response Content stream once consumes it; the next handler / caller would
/// see an empty body. We materialize into a string, log it, and replace the
/// Content with an equivalent StringContent the next reader can consume.
/// </summary>
internal sealed class DataverseLoggingHandler : DelegatingHandler
{
    private const int MaxBodyLogChars = 4000;

    private readonly IOptionsMonitor<DataverseOptions> _options;
    private readonly ILogger<DataverseLoggingHandler> _logger;

    public DataverseLoggingHandler(
        IOptionsMonitor<DataverseOptions> options,
        ILogger<DataverseLoggingHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.VerboseLogging)
        {
            // Pass-through. No body buffering, no extra log lines.
            return await base.SendAsync(request, cancellationToken);
        }

        var sw = Stopwatch.StartNew();

        // Buffer the request body so we can log + still send it.
        var requestBody = await ReadAndRebuildContentAsync(request.Content, cancellationToken);
        request.Content = requestBody.Rebuilt;

        _logger.LogInformation(
            "Dataverse → {Method} {Url}{BodyTrailer}",
            request.Method,
            request.RequestUri,
            FormatBodyForLog("Request", requestBody.Text));

        var response = await base.SendAsync(request, cancellationToken);
        sw.Stop();

        // Buffer the response body so we can log + still let the caller read it.
        var responseBody = await ReadAndRebuildContentAsync(response.Content, cancellationToken);
        response.Content = responseBody.Rebuilt;

        _logger.LogInformation(
            "Dataverse ← {Method} {Url} {Status} ({Duration} ms){BodyTrailer}",
            request.Method,
            request.RequestUri,
            (int)response.StatusCode,
            sw.ElapsedMilliseconds,
            FormatBodyForLog("Response", responseBody.Text));

        return response;
    }

    private static async Task<(string? Text, HttpContent? Rebuilt)> ReadAndRebuildContentAsync(
        HttpContent? content, CancellationToken cancellationToken)
    {
        if (content is null) return (null, null);

        var text = await content.ReadAsStringAsync(cancellationToken);
        // Recreate so the next reader (pipeline / caller) gets a fresh stream.
        var mediaType = content.Headers.ContentType?.MediaType ?? "application/json";
        var rebuilt = new StringContent(text, Encoding.UTF8, mediaType);
        return (text, rebuilt);
    }

    private static string FormatBodyForLog(string label, string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var snippet = text.Length <= MaxBodyLogChars
            ? text
            : text[..MaxBodyLogChars] + $"... [truncated, {text.Length - MaxBodyLogChars} more chars]";
        return $"\n  {label}: {snippet}";
    }
}
