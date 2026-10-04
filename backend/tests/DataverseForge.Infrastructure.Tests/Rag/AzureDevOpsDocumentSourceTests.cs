using System.Net;
using System.Text;
using DataverseForge.Application.Contracts;
using DataverseForge.Infrastructure.Rag;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataverseForge.Infrastructure.Tests.Rag;

/// <summary>
/// Unit tests for <see cref="AzureDevOpsDocumentSource"/>.
/// Uses a <see cref="FakeHttpMessageHandler"/> to mock AzDo REST API responses without network access.
/// </summary>
public sealed class AzureDevOpsDocumentSourceTests
{
    private const string OrgUrl = "https://dev.azure.com/myorg";
    private const string Project = "MyProject";

    private static AzureDevOpsDocumentSource CreateSource(FakeHttpMessageHandler handler) =>
        new(OrgUrl, Project, NullLogger<AzureDevOpsDocumentSource>.Instance,
            new HttpClient(handler));

    // ─── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDocumentsAsync_ReturnsDocumentForEachWorkItem()
    {
        // WIQL body contains the work item type — return id=42 only for Epic
        var handler = new FakeHttpMessageHandler()
            .OnPost((url, body) => url.Contains("wiql") && body.Contains("Epic"),
                """{"workItems":[{"id":42}]}""")
            .OnPost(url => url.Contains("wiql"),
                """{"workItems":[]}""")
            .OnGet(url => url.Contains("workitems?ids=42"),
                BuildWorkItemsResponse(42, "My Epic Title", "Epic", "Active", "Epic description text"));

        var source = CreateSource(handler);

        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Single(docs);
        Assert.Equal("azdo-backlog", docs[0].SourceId);
        Assert.Contains("My Epic Title", docs[0].Content);
        Assert.Contains("Epic description text", docs[0].Content);
        Assert.Contains("[Epic] #42:", docs[0].Content);
    }

    [Fact]
    public async Task GetDocumentsAsync_StripHtml_FromDescriptionAndAcceptanceCriteria()
    {
        var handler = new FakeHttpMessageHandler()
            .OnPost((url, body) => url.Contains("wiql") && body.Contains("User Story"),
                """{"workItems":[{"id":10}]}""")
            .OnPost(url => url.Contains("wiql"),
                """{"workItems":[]}""")
            .OnGet(url => url.Contains("workitems?ids=10"),
                BuildWorkItemsResponse(10, "Story Title", "User Story", "Active",
                    description: "<p>This is <b>HTML</b> content &amp; special chars</p>",
                    acceptance: "<ul><li>Criterion 1</li><li>Criterion 2</li></ul>"));

        var source = CreateSource(handler);

        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Single(docs);
        Assert.DoesNotContain("<p>", docs[0].Content);
        Assert.DoesNotContain("<b>", docs[0].Content);
        Assert.Contains("HTML", docs[0].Content);
        Assert.Contains("special chars", docs[0].Content);  // &amp; → &
        Assert.Contains("Criterion 1", docs[0].Content);
    }

    [Fact]
    public async Task GetDocumentsAsync_IncrementalFilter_PassesChangedSinceInWiql()
    {
        var capturedRequests = new List<string>();
        var handler = new FakeHttpMessageHandler()
            .OnPost(url => url.Contains("wiql"), """{"workItems":[]}""",
                captureUrl: u => capturedRequests.Add(u));

        var source = CreateSource(handler);
        var since = new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero);

        _ = await source.GetDocumentsAsync(changedSince: since).ToListAsync();

        // All 5 WIQL calls should include the ChangedDate filter
        Assert.Equal(5, capturedRequests.Count);
        // Verify by checking the POST bodies contained the date filter
        Assert.All(handler.CapturedPostBodies,
            body => Assert.Contains("2024-03-15", body));
    }

    [Fact]
    public async Task GetDocumentsAsync_WiqlReturnsEmpty_YieldsNoDocuments()
    {
        var handler = new FakeHttpMessageHandler()
            .OnPost(url => url.Contains("wiql"),
                """{"workItems":[]}""");

        var source = CreateSource(handler);

        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Empty(docs);
    }

    [Fact]
    public async Task GetDocumentsAsync_HttpError_ContinuesWithNextType()
    {
        // WIQL for Epic fails with 500; all others return empty
        var handler = new FakeHttpMessageHandler()
            .OnPost(url => url.Contains("Epic"),
                null, statusCode: HttpStatusCode.InternalServerError)
            .OnPost(url => url.Contains("wiql"),
                """{"workItems":[]}""");

        var source = CreateSource(handler);

        // Should not throw — error is swallowed, subsequent types still processed
        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Empty(docs);
    }

    [Fact]
    public async Task GetDocumentsAsync_FilePath_ContainsProjectAndTypeAndId()
    {
        var handler = new FakeHttpMessageHandler()
            .OnPost((url, body) => url.Contains("wiql") && body.Contains("Feature"),
                """{"workItems":[{"id":99}]}""")
            .OnPost(url => url.Contains("wiql"),
                """{"workItems":[]}""")
            .OnGet(url => url.Contains("workitems?ids=99"),
                BuildWorkItemsResponse(99, "Feature X", "Feature", "New"));

        var source = CreateSource(handler);

        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Single(docs);
        Assert.Equal("azdo://MyProject/Feature/99", docs[0].FilePath);
    }

    // ─── Static helpers ───────────────────────────────────────────────────────

    private static string BuildWorkItemsResponse(
        int id, string title, string type, string state,
        string description = "", string acceptance = "")
    {
        var escapedDescription = description.Replace("\"", "\\\"");
        var escapedAcceptance = acceptance.Replace("\"", "\\\"");
        return $$"""
            {
                "value": [{
                    "id": {{id}},
                    "fields": {
                        "System.Id": {{id}},
                        "System.Title": "{{title}}",
                        "System.WorkItemType": "{{type}}",
                        "System.State": "{{state}}",
                        "System.Description": "{{escapedDescription}}",
                        "Microsoft.VSTS.Common.AcceptanceCriteria": "{{escapedAcceptance}}",
                        "System.Tags": "",
                        "System.ChangedDate": "2024-01-01T00:00:00Z"
                    }
                }]
            }
            """;
    }
}

/// <summary>
/// Minimal fake <see cref="HttpMessageHandler"/> for unit tests.
/// Rules are evaluated in registration order; first match wins.
/// POST rules can inspect both the URL and the request body.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly record struct Rule(
        HttpMethod Method,
        Func<string, string, bool> Predicate,  // (url, body) — body is empty for GET
        string? ResponseBody,
        HttpStatusCode StatusCode,
        Action<string>? CaptureUrl);

    private readonly List<Rule> _rules = [];
    public List<string> CapturedPostBodies { get; } = [];

    /// <summary>Adds a POST rule. <paramref name="predicate"/> receives (url, body).</summary>
    public FakeHttpMessageHandler OnPost(Func<string, string, bool> predicate, string? responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK, Action<string>? captureUrl = null)
    {
        _rules.Add(new Rule(HttpMethod.Post, predicate, responseBody, statusCode, captureUrl));
        return this;
    }

    /// <summary>Adds a POST rule that checks only the URL.</summary>
    public FakeHttpMessageHandler OnPost(Func<string, bool> urlPredicate, string? responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK, Action<string>? captureUrl = null)
        => OnPost((url, _) => urlPredicate(url), responseBody, statusCode, captureUrl);

    /// <summary>Adds a GET rule that checks only the URL.</summary>
    public FakeHttpMessageHandler OnGet(Func<string, bool> urlPredicate, string? responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _rules.Add(new Rule(HttpMethod.Get, (url, _) => urlPredicate(url), responseBody, statusCode, null));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        var body = string.Empty;

        if (request.Method == HttpMethod.Post && request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
            CapturedPostBodies.Add(body);
        }

        foreach (var rule in _rules)
        {
            if (rule.Method != request.Method) continue;
            if (!rule.Predicate(url, body)) continue;

            rule.CaptureUrl?.Invoke(url);

            if (rule.StatusCode != HttpStatusCode.OK)
                return new HttpResponseMessage(rule.StatusCode);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(rule.ResponseBody ?? "{}", Encoding.UTF8, "application/json")
            };
        }

        // No rule matched — fail the test clearly
        throw new InvalidOperationException($"No rule matched {request.Method} {url}");
    }
}
