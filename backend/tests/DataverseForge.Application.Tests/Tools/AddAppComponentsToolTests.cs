using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class AddAppComponentsToolTests
{
    private static readonly Guid AppId = Guid.NewGuid();
    private static readonly Guid AccountMetadataId = Guid.Parse("70816501-edb9-4740-a16c-6a5efbc05d84");
    private static readonly Guid ContactMetadataId = Guid.Parse("608861bc-50a4-4c5f-a02c-21fe1943e2cf");

    private static (AddAppComponentsTool Tool, Mock<IDataverseClient> Client, Dictionary<string, ProjectState> Stored) BuildTool()
    {
        var stored = new Dictionary<string, ProjectState>();
        stored["test-session"] = new ProjectState { ProjectId = "test-session" };

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
        // Default: account + contact resolve; everything else returns null.
        client.Setup(c => c.GetEntityMetadataIdAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)AccountMetadataId);
        client.Setup(c => c.GetEntityMetadataIdAsync("contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)ContactMetadataId);

        var tool = new AddAppComponentsTool(client.Object, store.Object, ctx.Object,
            NullLogger<AddAppComponentsTool>.Instance);
        return (tool, client, stored);
    }

    [Fact]
    public async Task Add_TableComponent_ResolvesLogicalNameToMetadataId()
    {
        var (tool, client, _) = BuildTool();
        IReadOnlyList<AppComponentRef>? captured = null;
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) => captured = refs)
            .Returns(Task.CompletedTask);

        var json = """[{"type":"table","id":"account"}]""";
        var outcomes = await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Single(outcomes);
        Assert.True(outcomes[0].Succeeded);
        Assert.NotNull(captured);
        Assert.Single(captured!);
        Assert.Equal("entity", captured![0].Kind);
        Assert.Equal(AccountMetadataId, captured[0].Id);
    }

    [Fact]
    public async Task Add_MultipleTables_BatchesIntoOneAddCall()
    {
        var (tool, client, _) = BuildTool();
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var json = """[{"type":"table","id":"account"},{"type":"table","id":"contact"}]""";
        var outcomes = await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.True(o.Succeeded));
        client.Verify(c => c.AddAppComponentsAsync(
            AppId,
            It.Is<IReadOnlyList<AppComponentRef>>(refs => refs.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Add_UnknownTable_ReportsFailureWithoutCallingAdd()
    {
        var (tool, client, _) = BuildTool();
        client.Setup(c => c.GetEntityMetadataIdAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var json = """[{"type":"table","id":"nonexistent"}]""";
        var outcomes = await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Single(outcomes);
        Assert.False(outcomes[0].Succeeded);
        Assert.Contains("does not exist", outcomes[0].Message);
        client.Verify(c => c.AddAppComponentsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Add_MixedValidAndInvalid_PartialSuccess()
    {
        // account resolves; "ghost" doesn't. The valid component should still ship;
        // the invalid one is reported per-component.
        var (tool, client, _) = BuildTool();
        client.Setup(c => c.GetEntityMetadataIdAsync("ghost", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var json = """[{"type":"table","id":"account"},{"type":"table","id":"ghost"}]""";
        var outcomes = await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.Single(outcomes, o => o.Succeeded && o.Id == "account");
        Assert.Single(outcomes, o => !o.Succeeded && o.Id == "ghost");
    }

    [Theory]
    [InlineData("form")]
    [InlineData("view")]
    [InlineData("chart")]
    [InlineData("dashboard")]
    [InlineData("sitemap")]
    public async Task Add_NonTableComponent_PassesThroughGuid(string type)
    {
        var (tool, client, _) = BuildTool();
        IReadOnlyList<AppComponentRef>? captured = null;
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) => captured = refs)
            .Returns(Task.CompletedTask);

        var rowGuid = Guid.NewGuid();
        var json = $$"""[{"type":"{{type}}","id":"{{rowGuid}}"}]""";
        var outcomes = await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Single(outcomes);
        Assert.True(outcomes[0].Succeeded);
        Assert.NotNull(captured);
        Assert.Single(captured!);
        // For non-tables, the metadata-id resolver must NOT be called.
        client.Verify(c => c.GetEntityMetadataIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(rowGuid, captured![0].Id);
    }

    [Fact]
    public async Task Add_NonTableWithBadGuid_ReportsFailure()
    {
        var (tool, _, _) = BuildTool();

        var json = """[{"type":"form","id":"not-a-guid"}]""";
        var outcomes = await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Single(outcomes);
        Assert.False(outcomes[0].Succeeded);
        Assert.Contains("must be a valid GUID", outcomes[0].Message);
    }

    [Fact]
    public async Task Add_RejectsInvalidAppId()
    {
        var (tool, _, _) = BuildTool();

        var outcomes = await tool.AddAppComponentsAsync("not-a-guid",
            """[{"type":"table","id":"account"}]""",
            CancellationToken.None);

        Assert.Single(outcomes);
        Assert.False(outcomes[0].Succeeded);
        Assert.Contains("not a valid GUID", outcomes[0].Message);
    }

    [Fact]
    public async Task Add_RejectsMalformedJson()
    {
        var (tool, _, _) = BuildTool();

        var outcomes = await tool.AddAppComponentsAsync(AppId.ToString(), "{ not json", CancellationToken.None);

        Assert.Single(outcomes);
        Assert.False(outcomes[0].Succeeded);
        Assert.Contains("not valid JSON", outcomes[0].Message);
    }

    [Fact]
    public async Task Add_DuplicateTable_LooksUpMetadataIdOnce()
    {
        // Real-world: agent specifies the same table twice (e.g. once explicitly,
        // once as a fallback). We should cache the resolved MetadataId.
        var (tool, client, _) = BuildTool();
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var json = """[{"type":"table","id":"account"},{"type":"table","id":"ACCOUNT"}]""";
        await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        client.Verify(c => c.GetEntityMetadataIdAsync("account", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Add_SurfacesDataverseErrorPerComponentOnFailure()
    {
        var (tool, client, _) = BuildTool();
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("AddAppComponents failed: HTTP 400 — One of the components is already attached."));

        var json = """[{"type":"table","id":"account"}]""";
        var outcomes = await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Single(outcomes);
        Assert.False(outcomes[0].Succeeded);
        Assert.Contains("already attached", outcomes[0].Message);
    }

    [Fact]
    public async Task Add_UpdatesProjectStateOperationStackOnSuccess()
    {
        var (tool, client, stored) = BuildTool();
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await tool.AddAppComponentsAsync(AppId.ToString(),
            """[{"type":"table","id":"account"}]""",
            CancellationToken.None);

        var state = stored["test-session"];
        Assert.Single(state.OperationStack);
        Assert.Equal("AddAppComponents", state.OperationStack.Peek().ToolName);
    }
}
