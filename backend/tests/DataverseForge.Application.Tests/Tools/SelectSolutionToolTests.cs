using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class SelectSolutionToolTests
{
    private readonly Mock<IDataverseClient> _client = new();
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ISessionContext> _ctx = new();
    private readonly ProjectState _state = new() { ProjectId = "test" };

    public SelectSolutionToolTests()
    {
        _ctx.SetupGet(c => c.SessionId).Returns("test");
        _store.Setup(s => s.GetOrCreateAsync("test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_state);
        _store.Setup(s => s.SaveAsync("test", It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private SelectSolutionTool BuildTool() =>
        new(_client.Object, _store.Object, _ctx.Object, NullLogger<SelectSolutionTool>.Instance);

    private static SolutionInfo Sol(
        string unique, string display, string prefix = "test",
        Guid? id = null, Guid? pubId = null) =>
        new(
            SolutionId: id ?? Guid.NewGuid(),
            UniqueName: unique,
            DisplayName: display,
            Version: "1.0.0.0",
            PublisherName: prefix + " Publisher",
            PublisherId: pubId ?? Guid.NewGuid(),
            PublisherUniqueName: prefix,
            PublisherPrefix: prefix,
            IsManaged: false,
            ComponentCount: 0,
            ModifiedOn: DateTime.UtcNow);

    private void StubSolutions(params SolutionInfo[] solutions)
    {
        _client.Setup(c => c.GetSolutionsListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(solutions);
    }

    // ─── Happy paths: each resolution rung in the priority order ───

    [Fact]
    public async Task GuidMatch_ShortCircuits_AboveAllOtherSteps()
    {
        var sid = Guid.NewGuid();
        StubSolutions(
            Sol("OtherSolution", "Other", id: Guid.NewGuid()),
            Sol("EPPCLiveDemo", "EPPCLiveDemo", id: sid));

        // Pass a uniqueName that would match Other if we fell through to step 2,
        // but the GUID points at EPPCLiveDemo — GUID wins.
        var result = await BuildTool().SelectSolutionAsync(
            solutionId: sid.ToString(),
            uniqueName: "OtherSolution",
            displayName: "Wrong",
            version: "0.0",
            publisherId: Guid.NewGuid().ToString(),
            publisherDisplayName: "Wrong",
            publisherPrefix: "wrong",
            cancellationToken: CancellationToken.None);

        Assert.Contains("Active solution set to 'EPPCLiveDemo'", result, StringComparison.Ordinal);
        Assert.Equal(sid, _state.SolutionId);
    }

    [Fact]
    public async Task ExactUniqueNameMatch_WinsOverPartial_CaseInsensitive()
    {
        StubSolutions(
            Sol("GiftDashboardSolution", "Gift Dashboard Solution", "eppc"),
            Sol("EPPCLiveDemo", "EPPCLiveDemo", "eppc"));

        // Lower-case input — must still pick the exact uniqueName.
        var result = await BuildTool().SelectSolutionAsync(
            solutionId: Guid.Empty.ToString(),
            uniqueName: "eppclivedemo",
            displayName: "",
            version: "",
            publisherId: Guid.Empty.ToString(),
            publisherDisplayName: "",
            publisherPrefix: "",
            cancellationToken: CancellationToken.None);

        Assert.Contains("EPPCLiveDemo", result, StringComparison.Ordinal);
        Assert.Equal("EPPCLiveDemo", _state.SolutionUniqueName);
    }

    [Fact]
    public async Task DisplayNameMatch_WhenAgentPassesFriendlyNameAsUniqueName()
    {
        StubSolutions(
            Sol("EPPCLiveDemo", "EPPC Live Demo"));

        var result = await BuildTool().SelectSolutionAsync(
            solutionId: Guid.Empty.ToString(),
            uniqueName: "EPPC Live Demo",
            displayName: "",
            version: "",
            publisherId: Guid.Empty.ToString(),
            publisherDisplayName: "",
            publisherPrefix: "",
            cancellationToken: CancellationToken.None);

        Assert.Contains("Active solution set to", result, StringComparison.Ordinal);
        Assert.Equal("EPPCLiveDemo", _state.SolutionUniqueName);
    }

    [Fact]
    public async Task PartialMatch_SingleCandidate_AutoSelects()
    {
        StubSolutions(
            Sol("OtherSolution", "Other", "other"),
            Sol("EPPCLiveDemo", "EPPCLiveDemo", "eppc"));

        // "Live" only appears in EPPCLiveDemo — partial match resolves cleanly.
        var result = await BuildTool().SelectSolutionAsync(
            solutionId: Guid.Empty.ToString(),
            uniqueName: "Live",
            displayName: "", version: "",
            publisherId: Guid.Empty.ToString(),
            publisherDisplayName: "", publisherPrefix: "",
            cancellationToken: CancellationToken.None);

        Assert.Contains("EPPCLiveDemo", result, StringComparison.Ordinal);
    }

    // ─── The actionable-error rung — Fix 3's main behavior ───

    [Fact]
    public async Task PrefixSharedByTwo_ReturnsActionableErrorWithUniqueNames()
    {
        StubSolutions(
            Sol("GiftDashboardSolution", "Gift Dashboard Solution", "eppc"),
            Sol("EPPCLiveDemo", "EPPCLiveDemo", "eppc"));

        var result = await BuildTool().SelectSolutionAsync(
            solutionId: Guid.Empty.ToString(),
            uniqueName: "eppc",
            displayName: "", version: "",
            publisherId: Guid.Empty.ToString(),
            publisherDisplayName: "", publisherPrefix: "",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("ERROR:", result, StringComparison.Ordinal);
        // The error MUST mechanically point the agent at the right next call.
        Assert.Contains("\"GiftDashboardSolution\"", result, StringComparison.Ordinal);
        Assert.Contains("\"EPPCLiveDemo\"", result, StringComparison.Ordinal);
        Assert.Contains("Retry with one of these exact uniqueName values", result, StringComparison.Ordinal);
        // And explicitly tell the agent NOT to re-call with the same input.
        Assert.Contains("Do NOT re-call with 'eppc'", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialMatchTwoCandidates_ReturnsActionableErrorWithUniqueNames()
    {
        // Different prefixes so the prefix branch can't catch it; rely on the
        // partial-Contains rung instead.
        StubSolutions(
            Sol("DemoSolutionOne", "Demo Solution One", "alpha"),
            Sol("DemoSolutionTwo", "Demo Solution Two", "beta"));

        var result = await BuildTool().SelectSolutionAsync(
            solutionId: Guid.Empty.ToString(),
            uniqueName: "Demo",
            displayName: "", version: "",
            publisherId: Guid.Empty.ToString(),
            publisherDisplayName: "", publisherPrefix: "",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("ERROR:", result, StringComparison.Ordinal);
        Assert.Contains("\"DemoSolutionOne\"", result, StringComparison.Ordinal);
        Assert.Contains("\"DemoSolutionTwo\"", result, StringComparison.Ordinal);
        Assert.Contains("Retry with one of these exact uniqueName values", result, StringComparison.Ordinal);
        Assert.Contains("partially", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrefixAmbiguity_ListMoreThanFive_TruncatesWithCounter()
    {
        var sols = Enumerable.Range(0, 7)
            .Select(i => Sol($"Sol{i}", $"Sol {i}", "shared"))
            .ToArray();
        StubSolutions(sols);

        var result = await BuildTool().SelectSolutionAsync(
            solutionId: Guid.Empty.ToString(),
            uniqueName: "shared",
            displayName: "", version: "",
            publisherId: Guid.Empty.ToString(),
            publisherDisplayName: "", publisherPrefix: "",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("ERROR:", result, StringComparison.Ordinal);
        Assert.Contains("(+2 more)", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoMatchAtAll_ReturnsNotFoundError()
    {
        StubSolutions(Sol("OnlyOne", "Only One"));

        var result = await BuildTool().SelectSolutionAsync(
            solutionId: Guid.Empty.ToString(),
            uniqueName: "DoesNotExist",
            displayName: "", version: "",
            publisherId: Guid.Empty.ToString(),
            publisherDisplayName: "", publisherPrefix: "",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("ERROR:", result, StringComparison.Ordinal);
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }
}
