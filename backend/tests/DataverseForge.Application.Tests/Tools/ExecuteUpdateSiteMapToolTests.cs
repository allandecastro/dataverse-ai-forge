using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

/// <summary>
/// Locks in the 2026-05-25 F-23 fix (ApplyAndSaveAsync) and the existing
/// GUID + ALREADY_EXECUTED guards on sitemap updates.
/// </summary>
public class ExecuteUpdateSiteMapToolTests
{
    private const string PlanId = "update_sitemap_test";
    private const string PayloadJson = "{\"sitemapxml\":\"<SiteMap/>\"}";
    private static readonly Guid SiteMapId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly Mock<IDataverseClient> _client = new();
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ISessionContext> _ctx = new();

    private ExecuteUpdateSiteMapTool CreateTool() => new(
        _client.Object, _store.Object, _ctx.Object,
        NullLogger<ExecuteUpdateSiteMapTool>.Instance);

    private ProjectState Setup(string sessionId, bool pending = true, bool alreadyExecuted = false)
    {
        var state = new ProjectState { ProjectId = sessionId };
        if (pending) state.PendingPlans[PlanId] = PayloadJson;
        if (alreadyExecuted) state.ExecutedPlans[PlanId] = PayloadJson;
        _ctx.Setup(c => c.SessionId).Returns(sessionId);
        _store.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        return state;
    }

    [Fact]
    public async Task HappyPath_CallsUpdate_MovesPlanToExecuted_PushesOp()
    {
        var state = Setup("usm-1");
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateSiteMapAsync(
            PlanId, SiteMapId.ToString(), CancellationToken.None);

        _client.Verify(c => c.UpdateSiteMapAsync(
            SiteMapId, PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
        Assert.True(state.ExecutedPlans.ContainsKey(PlanId));
        Assert.Single(state.OperationStack);
        Assert.Contains(SiteMapId.ToString(), result);
    }

    [Fact]
    public async Task AlreadyExecuted_ShortCircuits()
    {
        Setup("usm-2", pending: false, alreadyExecuted: true);
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateSiteMapAsync(
            PlanId, SiteMapId.ToString(), CancellationToken.None);

        Assert.StartsWith("ALREADY_EXECUTED:", result);
        _client.Verify(c => c.UpdateSiteMapAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidSiteMapId_ReturnsError_WithoutLookup(string badId)
    {
        Setup("usm-3", pending: false);
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateSiteMapAsync(
            PlanId, badId, CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("siteMapId", result);
        _store.Verify(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlanNotFound_ReturnsError_WithoutCallingClient()
    {
        Setup("usm-4", pending: false);
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateSiteMapAsync(
            "missing_plan", SiteMapId.ToString(), CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _client.Verify(c => c.UpdateSiteMapAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
