using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

/// <summary>
/// Symmetric coverage with <see cref="AddAppComponentsToolTests"/> — both tools
/// share the same input shape, table-name → MetadataId resolution, and
/// per-component outcome envelope. Lighter than Add's tests because the
/// add-side covers the JSON-parsing edge cases.
/// </summary>
public class RemoveAppComponentsToolTests
{
    private static readonly Guid AppId = Guid.NewGuid();
    private static readonly Guid AccountMetadataId = Guid.Parse("70816501-edb9-4740-a16c-6a5efbc05d84");

    private static (RemoveAppComponentsTool Tool, Mock<IDataverseClient> Client) BuildTool()
    {
        var stored = new Dictionary<string, ProjectState> { ["test-session"] = new() };
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState(); stored[id] = s; }
                return s;
            });
        store.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");
        var client = new Mock<IDataverseClient>();
        client.Setup(c => c.GetEntityMetadataIdAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)AccountMetadataId);
        var tool = new RemoveAppComponentsTool(client.Object, store.Object, ctx.Object,
            NullLogger<RemoveAppComponentsTool>.Instance);
        return (tool, client);
    }

    [Fact]
    public async Task Remove_TableComponent_ResolvesLogicalNameAndCallsClient()
    {
        var (tool, client) = BuildTool();
        IReadOnlyList<AppComponentRef>? captured = null;
        client.Setup(c => c.RemoveAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) =>
            {
                captured = refs;
                return refs.Select(r => new AppComponentRefOutcome(r, true, null)).ToList().AsReadOnly();
            });

        var json = """[{"type":"table","id":"account"}]""";
        var outcomes = await tool.RemoveAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Single(outcomes);
        Assert.True(outcomes[0].Succeeded);
        Assert.NotNull(captured);
        Assert.Equal("entity", captured![0].Kind);
        Assert.Equal(AccountMetadataId, captured[0].Id);
    }

    [Fact]
    public async Task Remove_FormByGuid_PassesThrough()
    {
        var (tool, client) = BuildTool();
        var formId = Guid.NewGuid();
        IReadOnlyList<AppComponentRef>? captured = null;
        client.Setup(c => c.RemoveAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) =>
            {
                captured = refs;
                return refs.Select(r => new AppComponentRefOutcome(r, true, null)).ToList().AsReadOnly();
            });

        var json = $$"""[{"type":"form","id":"{{formId}}"}]""";
        var outcomes = await tool.RemoveAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Single(outcomes);
        Assert.True(outcomes[0].Succeeded);
        Assert.Equal(formId, captured![0].Id);
        Assert.Equal("form", captured[0].Kind);
    }

    [Fact]
    public async Task Remove_RejectsInvalidAppId()
    {
        var (tool, client) = BuildTool();

        var outcomes = await tool.RemoveAppComponentsAsync("not-a-guid",
            """[{"type":"table","id":"account"}]""",
            CancellationToken.None);

        Assert.Single(outcomes);
        Assert.False(outcomes[0].Succeeded);
        client.Verify(c => c.RemoveAppComponentsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Remove_SurfacesDataverseErrorVerbatim()
    {
        // Client returns per-ref outcomes including failures (rather than
        // throwing) — exercise that path. Throw path covered separately.
        var (tool, client) = BuildTool();
        client.Setup(c => c.RemoveAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) =>
                refs.Select(r => new AppComponentRefOutcome(r, false,
                    "RemoveAppComponents failed: HTTP 404 — Component not found on the app.")).ToList().AsReadOnly());

        var outcomes = await tool.RemoveAppComponentsAsync(AppId.ToString(),
            """[{"type":"table","id":"account"}]""",
            CancellationToken.None);

        Assert.Single(outcomes);
        Assert.False(outcomes[0].Succeeded);
        Assert.Contains("not found on the app", outcomes[0].Message);
    }

    [Fact]
    public async Task Remove_ClientThrows_AllRefsMarkedFailed()
    {
        var (tool, client) = BuildTool();
        client.Setup(c => c.RemoveAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("client misbehaved"));

        var outcomes = await tool.RemoveAppComponentsAsync(AppId.ToString(),
            """[{"type":"table","id":"account"}]""",
            CancellationToken.None);

        Assert.Single(outcomes);
        Assert.False(outcomes[0].Succeeded);
        Assert.Contains("client misbehaved", outcomes[0].Message);
    }
}
