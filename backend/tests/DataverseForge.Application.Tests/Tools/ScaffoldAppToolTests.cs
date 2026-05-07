using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ScaffoldAppToolTests
{
    private static (ScaffoldAppTool Tool, Mock<IDataverseClient> Client, ProjectState State) BuildTool()
    {
        var state = new ProjectState
        {
            ProjectId = "test-session",
            SolutionUniqueName = "EPPCLiveDemo",
            PublisherPrefix = "eppc",
        };
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        store.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");
        var client = new Mock<IDataverseClient>();
        var tool = new ScaffoldAppTool(client.Object, store.Object, ctx.Object, NullLogger<ScaffoldAppTool>.Instance);
        return (tool, client, state);
    }

    private static void StubHappyPath(Mock<IDataverseClient> client, Guid appId, params (string Name, Guid Id)[] entities)
    {
        foreach (var e in entities)
        {
            client.Setup(c => c.GetEntityMetadataIdAsync(e.Name, It.IsAny<CancellationToken>()))
                .ReturnsAsync(e.Id);
        }
        client.Setup(c => c.ResolveWebResourceIdByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        client.Setup(c => c.CreateAppAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appId);
        client.Setup(c => c.CreateSiteMapAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        client.Setup(c => c.AddAppComponentsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) =>
                refs.Select(r => new AppComponentRefOutcome(r, true, null)).ToList().AsReadOnly());
        client.Setup(c => c.PublishAppAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        client.Setup(c => c.AssignAppRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Scaffold_HappyPath_RunsAllSteps()
    {
        var (tool, client, state) = BuildTool();
        var appId = Guid.NewGuid();
        StubHappyPath(client, appId,
            ("account", Guid.NewGuid()), ("contact", Guid.NewGuid()), ("eppc_gift", Guid.NewGuid()));

        var roleId = Guid.NewGuid();
        var result = await tool.ScaffoldAppAsync(
            "Gift Hub", "eppc_gifthub", "demo app",
            "account,contact,eppc_gift",
            roleId.ToString(),
            CancellationToken.None);

        Assert.True(result.OverallSucceeded);
        Assert.Equal(appId, result.AppId);
        Assert.NotNull(result.SiteMapId);
        // 1 validate + 1 resolve + 1 create_app + 1 create_sitemap + 1 attach + 1 publish + 1 assign_role.
        Assert.Equal(7, result.Steps.Count);
        Assert.All(result.Steps, s => Assert.True(s.Succeeded));

        // Apps tracked in state for the sidebar.
        Assert.Single(state.Apps, a => a.AppModuleId == appId && a.UniqueName == "eppc_gifthub");
        // Reverse op points at TeardownApp.
        Assert.Equal("TeardownApp", state.OperationStack.Peek().ReverseOp!.ToolName);
    }

    [Fact]
    public async Task Scaffold_UnknownTable_HardFailsBeforeCreate()
    {
        var (tool, client, _) = BuildTool();
        client.Setup(c => c.GetEntityMetadataIdAsync("account", It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        // "missing_table" → null → unknown.
        client.Setup(c => c.GetEntityMetadataIdAsync("missing_table", It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        var result = await tool.ScaffoldAppAsync(
            "X", "eppc_x", "x",
            "account,missing_table",
            null,
            CancellationToken.None);

        Assert.False(result.OverallSucceeded);
        // Did NOT call create_app — fail early when an entity is unknown.
        client.Verify(c => c.CreateAppAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(result.Steps, s => s.Step == "resolve_tables" && !s.Succeeded);
        Assert.Contains("missing_table", result.Steps.Single(s => s.Step == "resolve_tables").Detail);
    }

    [Fact]
    public async Task Scaffold_CreateAppFails_StopsAndDoesNotAttachOrPublish()
    {
        var (tool, client, _) = BuildTool();
        client.Setup(c => c.GetEntityMetadataIdAsync("account", It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        client.Setup(c => c.ResolveWebResourceIdByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        client.Setup(c => c.CreateAppAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse 400 — uniquename already exists"));

        var result = await tool.ScaffoldAppAsync(
            "X", "eppc_x", "x",
            "account",
            null,
            CancellationToken.None);

        Assert.False(result.OverallSucceeded);
        Assert.Contains(result.Steps, s => s.Step == "create_app" && !s.Succeeded);
        // Sitemap / attach / publish all skipped.
        client.Verify(c => c.CreateSiteMapAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.AddAppComponentsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.PublishAppAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Scaffold_RoleAssignFailure_DoesNotAbortOtherSteps()
    {
        var (tool, client, _) = BuildTool();
        var appId = Guid.NewGuid();
        StubHappyPath(client, appId, ("account", Guid.NewGuid()));
        // First role succeeds, second fails — neither should abort each other.
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        client.Setup(c => c.AssignAppRoleAsync(appId, roleA, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        client.Setup(c => c.AssignAppRoleAsync(appId, roleB, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("403 — privilege missing"));

        var result = await tool.ScaffoldAppAsync(
            "X", "eppc_x", "x",
            "account",
            $"{roleA},{roleB}",
            CancellationToken.None);

        // Overall fails (one role failed), but the per-step shape captures
        // BOTH role outcomes so the agent can quote precisely.
        Assert.False(result.OverallSucceeded);
        Assert.Contains(result.Steps, s => s.Step == $"assign_role:{roleA}" && s.Succeeded);
        Assert.Contains(result.Steps, s => s.Step == $"assign_role:{roleB}" && !s.Succeeded);
        // Publish ran AFTER the first assign failure (loop order: publish before roles).
        client.Verify(c => c.PublishAppAsync(appId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Scaffold_IconResolutionReturnsEmpty_PreFailsBeforeCreate()
    {
        // F-13: when the OOB default icon web resource isn't resolvable, the
        // appmodule create would fail with a confusing "webresourceid cannot
        // be NULL" error. Scaffold must pre-fail with a clear message and
        // never call CreateAppAsync. Mirror of plan_create_app's pre-fail.
        var (tool, client, _) = BuildTool();
        client.Setup(c => c.GetEntityMetadataIdAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        client.Setup(c => c.ResolveWebResourceIdByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await tool.ScaffoldAppAsync(
            "X", "eppc_x", "x",
            "account",
            null,
            CancellationToken.None);

        Assert.False(result.OverallSucceeded);
        Assert.Contains(result.Steps, s => s.Step == "resolve_icon" && !s.Succeeded);
        client.Verify(c => c.CreateAppAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Scaffold_PartialAttachFailure_FlagsFailedComponentsByName()
    {
        // F-10 partial-success: one of the entity attaches fails. Scaffold's
        // "attach_components" step must report the actual failed ref instead
        // of marking everything failed (the prior behavior).
        var (tool, client, _) = BuildTool();
        var appId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        StubHappyPath(client, appId, ("account", accountId), ("contact", contactId));
        client.Setup(c => c.AddAppComponentsAsync(appId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> refs, CancellationToken _) =>
            {
                // Mark the LAST entity as failed (sitemap + account succeed, contact fails).
                var outcomes = new List<AppComponentRefOutcome>();
                for (var i = 0; i < refs.Count; i++)
                {
                    var ok = i < refs.Count - 1;
                    outcomes.Add(new AppComponentRefOutcome(refs[i], ok, ok ? null : "entity attach 400"));
                }
                return outcomes.AsReadOnly();
            });

        var result = await tool.ScaffoldAppAsync(
            "X", "eppc_x", "x",
            "account,contact",
            null,
            CancellationToken.None);

        Assert.False(result.OverallSucceeded);
        var attachStep = result.Steps.Single(s => s.Step == "attach_components");
        Assert.False(attachStep.Succeeded);
        Assert.Contains("entity attach 400", attachStep.Detail);
    }

    [Fact]
    public async Task Scaffold_RejectsEmptyTableList()
    {
        var (tool, _, _) = BuildTool();
        var result = await tool.ScaffoldAppAsync("X", "eppc_x", "x", "  ,  ,", null, CancellationToken.None);

        Assert.False(result.OverallSucceeded);
        Assert.Contains(result.Steps, s => !s.Succeeded && s.Step == "validate");
    }

    [Fact]
    public void BuildDefaultSitemap_OneAreaOneGroupOneSubAreaPerTable()
    {
        var spec = ScaffoldAppTool.BuildDefaultSitemap("eppc_gifthub", ["account", "contact", "eppc_gift"]);

        Assert.Single(spec.Areas);
        var area = spec.Areas[0];
        Assert.True(area.ShowGroups);
        Assert.Single(area.Groups);
        var group = area.Groups[0];
        Assert.Equal(3, group.SubAreas.Count);
        var entities = group.SubAreas.Select(s => s.Entity ?? "").ToArray();
        Assert.Equal(new[] { "account", "contact", "eppc_gift" }, entities);
        // Each subarea points to the entity (logical name) — not URL/PageId.
        Assert.All(group.SubAreas, s => { Assert.Null(s.Url); Assert.Null(s.PageId); });
    }
}
