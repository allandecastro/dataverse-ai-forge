using System.Net;
using System.Net.Http.Headers;
using DataverseForge.Infrastructure.Dataverse;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataverseForge.Infrastructure.Tests.Dataverse;

/// <summary>
/// Confirms the Dataverse transient-fault policy (429 / 503 / 504 with
/// Retry-After + exponential backoff) actually retries, gives up cleanly, and
/// forwards the request body across retries. Uses <c>Retry-After: 0</c> so the
/// honored delay is instant and the tests stay fast.
/// </summary>
public class DataverseRetryHandlerTests
{
    private sealed class RecordingStub(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public int Calls { get; private set; }
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static HttpResponseMessage Retryable(HttpStatusCode code) =>
        new(code) { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero) } };

    private static HttpMessageInvoker Invoker(RecordingStub stub) =>
        new(new DataverseRetryHandler(NullLogger<DataverseRetryHandler>.Instance) { InnerHandler = stub });

    [Fact]
    public async Task Retries_429_ThenReturnsSuccess()
    {
        var stub = new RecordingStub(Retryable(HttpStatusCode.TooManyRequests), new HttpResponseMessage(HttpStatusCode.OK));

        var resp = await Invoker(stub).SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://x/"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, stub.Calls);
    }

    [Fact]
    public async Task DoesNotRetry_NonRetryableStatus()
    {
        var stub = new RecordingStub(new HttpResponseMessage(HttpStatusCode.BadRequest));

        var resp = await Invoker(stub).SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://x/"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task ExhaustsRetries_ReturnsLastResponse()
    {
        // initial attempt + 3 retries = 4 calls, all 503.
        var stub = new RecordingStub(
            Retryable(HttpStatusCode.ServiceUnavailable),
            Retryable(HttpStatusCode.ServiceUnavailable),
            Retryable(HttpStatusCode.ServiceUnavailable),
            Retryable(HttpStatusCode.ServiceUnavailable));

        var resp = await Invoker(stub).SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://x/"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        Assert.Equal(4, stub.Calls);
    }

    [Fact]
    public async Task ForwardsRequestBody_AcrossRetries()
    {
        var stub = new RecordingStub(Retryable(HttpStatusCode.TooManyRequests), new HttpResponseMessage(HttpStatusCode.OK));
        var req = new HttpRequestMessage(HttpMethod.Post, "https://x/") { Content = new StringContent("{\"a\":1}") };

        await Invoker(stub).SendAsync(req, CancellationToken.None);

        Assert.Equal(2, stub.Calls);
        Assert.All(stub.Bodies, b => Assert.Equal("{\"a\":1}", b));
    }
}
