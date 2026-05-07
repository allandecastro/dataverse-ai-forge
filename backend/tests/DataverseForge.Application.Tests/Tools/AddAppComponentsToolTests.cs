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

    /// <summary>
    /// Build the per-ref outcome list the new client contract returns —
    /// every input ref maps to a Succeeded outcome. Mirrors the happy
    /// path Dataverse returns when AddAppComponents accepts the batch.
    /// </summary>
    private static IReadOnlyList<AppComponentRefOutcome> AllSucceeded(IReadOnlyList<AppComponentRef> refs)
        => refs.Select(r => new AppComponentRefOutcome(r, true, null)).ToList().AsReadOnly();

    [Fact]
    public async Task Add_TableComponent_ResolvesLogicalNameToMetadataId()
    {
        var (tool, client, _) = BuildTool();
        IReadOnlyList<AppComponentRef>? captured = null;
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) =>
            {
                captured = refs;
                return AllSucceeded(refs);
            });

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
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) => AllSucceeded(refs));

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
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) => AllSucceeded(refs));

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
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) =>
            {
                captured = refs;
                return AllSucceeded(refs);
            });

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
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) => AllSucceeded(refs));

        var json = """[{"type":"table","id":"account"},{"type":"table","id":"ACCOUNT"}]""";
        await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        client.Verify(c => c.GetEntityMetadataIdAsync("account", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Add_SurfacesDataverseErrorPerComponentOnFailure()
    {
        // Client now reports per-ref failures via the outcome list rather than
        // throwing — exercise that path. (Throwing is the unexpected systemic
        // failure path, covered by Add_ClientThrows_AllRefsMarkedFailed.)
        var (tool, client, _) = BuildTool();
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) =>
                refs.Select(r => new AppComponentRefOutcome(r, false,
                    "AddAppComponents failed: HTTP 400 — One of the components is already attached.")).ToList().AsReadOnly());

        var json = """[{"type":"table","id":"account"}]""";
        var outcomes = await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Single(outcomes);
        Assert.False(outcomes[0].Succeeded);
        Assert.Contains("already attached", outcomes[0].Message);
    }

    [Fact]
    public async Task Add_ClientThrows_AllRefsMarkedFailed()
    {
        // Defensive: when the client throws (something the per-ref outcome
        // contract was supposed to catch), the tool still returns one
        // failure outcome per requested ref instead of bubbling the throw.
        var (tool, client, _) = BuildTool();
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("client misbehaved"));

        var json = """[{"type":"table","id":"account"},{"type":"table","id":"contact"}]""";
        var outcomes = await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.False(o.Succeeded));
        Assert.All(outcomes, o => Assert.Contains("client misbehaved", o.Message));
    }

    [Fact]
    public async Task Add_PartialSuccessFromClient_OnlySucceededShipsToOperationStack()
    {
        // Real-session scenario behind F-10: the bulk action succeeds for
        // sitemap+entity#1, but the per-entity nav-property POST for entity
        // #2 fails. The tool must (a) report per-ref outcomes truthfully and
        // (b) only push the SUCCEEDED refs into the OperationStack so undo
        // doesn't try to detach things that never attached.
        var (tool, client, stored) = BuildTool();
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) =>
                new List<AppComponentRefOutcome>
                {
                    new(refs[0], true, null),
                    new(refs[1], false, "entity 2 failed"),
                }.AsReadOnly());

        var json = """[{"type":"table","id":"account"},{"type":"table","id":"contact"}]""";
        var outcomes = await tool.AddAppComponentsAsync(AppId.ToString(), json, CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.True(outcomes[0].Succeeded);
        Assert.False(outcomes[1].Succeeded);

        var state = stored["test-session"];
        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        // The reverse-op should target only the ONE ref that succeeded.
        // Reflect on the anonymous Parameters bag to read `components` —
        // dynamic dispatch trips nullable analysis under TreatWarningsAsErrors.
        var reverseOp = op.ReverseOp;
        Assert.NotNull(reverseOp);
        var paramsObj = reverseOp.Parameters;
        Assert.NotNull(paramsObj);
        var componentsField = paramsObj.GetType().GetProperty("components");
        Assert.NotNull(componentsField);
        var components = (IEnumerable<AppComponentRef>)componentsField.GetValue(paramsObj)!;
        Assert.Single(components);
    }

    [Fact]
    public async Task Add_UpdatesProjectStateOperationStackOnSuccess()
    {
        var (tool, client, stored) = BuildTool();
        client.Setup(c => c.AddAppComponentsAsync(AppId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) => AllSucceeded(refs));

        await tool.AddAppComponentsAsync(AppId.ToString(),
            """[{"type":"table","id":"account"}]""",
            CancellationToken.None);

        var state = stored["test-session"];
        Assert.Single(state.OperationStack);
        Assert.Equal("AddAppComponents", state.OperationStack.Peek().ToolName);
    }
}
