using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Reads Azure DevOps work items (Epics, Features, User Stories, Tasks, Bugs)
/// and converts them to plain-text documents for RAG ingestion.
///
/// Uses the AzDo REST API v7 (no extra NuGet required — pure HttpClient).
/// Credentials: <c>AzureDevOpsOrganizationUrl</c>, <c>AzureDevOpsProject</c>,
/// <c>AzureDevOpsPersonalAccessToken</c> from user settings.
/// </summary>
public sealed class AzureDevOpsDocumentSource : IDocumentSource
{
    private readonly HttpClient _http;
    private readonly string _orgUrl;
    private readonly string _project;
    private readonly ILogger<AzureDevOpsDocumentSource> _logger;

    // Work item types to ingest — covers all levels of a D365 project backlog.
    private static readonly string[] WorkItemTypes = ["Epic", "Feature", "User Story", "Task", "Bug"];

    public AzureDevOpsDocumentSource(string orgUrl, string project, string pat, ILogger<AzureDevOpsDocumentSource> logger)
    {
        _orgUrl = orgUrl.TrimEnd('/');
        _project = project;
        _logger = logger;
        _http = CreateClient(pat);
    }

    /// <summary>Testing constructor — caller provides a pre-configured <see cref="HttpClient"/>.</summary>
    internal AzureDevOpsDocumentSource(string orgUrl, string project, ILogger<AzureDevOpsDocumentSource> logger, HttpClient httpClient)
    {
        _orgUrl = orgUrl.TrimEnd('/');
        _project = project;
        _logger = logger;
        _http = httpClient;
    }

    private static HttpClient CreateClient(string pat)
    {
        var client = new HttpClient();
        // AzDo PAT auth: username is ignored; password = PAT
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public string Id => "azdo-backlog";
    public string Name => "Backlog AzDo";
    public SourceCategory Category => SourceCategory.Backlog;
    /// <summary>AzDo source uses ChangedDate filter in WIQL to avoid re-fetching unchanged work items.</summary>
    public bool SupportsIncrementalFetch => true;

    public async IAsyncEnumerable<RawDocument> GetDocumentsAsync(
        DateTimeOffset? changedSince = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_orgUrl) || string.IsNullOrWhiteSpace(_project))
            yield break;

        foreach (var workItemType in WorkItemTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<int> ids;
            try
            {
                ids = await QueryWorkItemIdsAsync(workItemType, changedSince, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to query AzDo work items of type '{Type}'", workItemType);
                continue;
            }

            if (ids.Count == 0)
                continue;

            // Fetch in batches of 200 (AzDo API limit)
            for (var i = 0; i < ids.Count; i += 200)
            {
                var batch = ids.Skip(i).Take(200).ToList();
                List<JsonObject> items;

                try
                {
                    items = await FetchWorkItemsAsync(batch, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to fetch AzDo work item batch starting at {Offset}", i);
                    continue;
                }

                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var doc = WorkItemToDocument(item);
                    if (doc is not null)
                        yield return doc;
                }
            }
        }
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private async Task<List<int>> QueryWorkItemIdsAsync(string workItemType, DateTimeOffset? changedSince, CancellationToken ct)
    {
        var changedFilter = changedSince.HasValue
            ? $" AND [System.ChangedDate] >= '{changedSince.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}'"
            : string.Empty;

        var wiql = $$"""
            {
                "query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{{_project}}' AND [System.WorkItemType] = '{{workItemType}}' AND [System.State] <> 'Removed'{{changedFilter}} ORDER BY [System.Id]"
            }
            """;

        var url = $"{_orgUrl}/{Uri.EscapeDataString(_project)}/_apis/wit/wiql?api-version=7.0";
        var response = await _http.PostAsync(url, new StringContent(wiql, Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return json?["workItems"]?.AsArray()
            .Select(x => x!["id"]!.GetValue<int>())
            .ToList() ?? [];
    }

    private async Task<List<JsonObject>> FetchWorkItemsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var idList = string.Join(",", ids);
        var fields = "System.Id,System.Title,System.Description,System.WorkItemType,System.State,Microsoft.VSTS.Common.AcceptanceCriteria,System.Tags,System.ChangedDate";
        var url = $"{_orgUrl}/_apis/wit/workitems?ids={idList}&fields={fields}&api-version=7.0";

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return json?["value"]?.AsArray()
            .OfType<JsonObject>()
            .ToList() ?? [];
    }

    private RawDocument? WorkItemToDocument(JsonObject item)
    {
        var fields = item["fields"]?.AsObject();
        if (fields is null) return null;

        var id = item["id"]?.GetValue<int>() ?? 0;
        var title = fields["System.Title"]?.GetValue<string>() ?? "(no title)";
        var type = fields["System.WorkItemType"]?.GetValue<string>() ?? "WorkItem";
        var state = fields["System.State"]?.GetValue<string>() ?? "";
        var description = fields["System.Description"]?.GetValue<string>() ?? "";
        var acceptance = fields["Microsoft.VSTS.Common.AcceptanceCriteria"]?.GetValue<string>() ?? "";
        var tags = fields["System.Tags"]?.GetValue<string>() ?? "";
        var changedDate = fields["System.ChangedDate"]?.GetValue<DateTime>() ?? DateTime.UtcNow;

        // Strip HTML from description and acceptance criteria
        description = StripHtml(description);
        acceptance = StripHtml(acceptance);

        var sb = new StringBuilder();
        sb.AppendLine($"[{type}] #{id}: {title}");
        sb.AppendLine($"State: {state}");
        if (!string.IsNullOrWhiteSpace(tags))
            sb.AppendLine($"Tags: {tags}");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($"Description: {description}");
        if (!string.IsNullOrWhiteSpace(acceptance))
            sb.AppendLine($"Acceptance Criteria: {acceptance}");

        var filePath = $"azdo://{_project}/{type}/{id}";
        return new RawDocument(Id, Name, filePath, sb.ToString(), "text",
            new DateTimeOffset(changedDate, TimeSpan.Zero));
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ")
            .Replace("&nbsp;", " ")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "&")
            .Trim();
    }
}
