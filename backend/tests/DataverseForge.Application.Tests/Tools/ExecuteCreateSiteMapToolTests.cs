using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateSiteMapToolTests
{
    private static (ExecuteCreateSiteMapTool Tool, Mock<IDataverseClient> Client, Dictionary<string, ProjectState> Stored, string PlanId) BuildTool(
        string solutionUniqueName = "EPPCLiveDemo")
    {
        var stored = new Dictionary<string, ProjectState>();
        var planId = $"create_sitemap_{Guid.NewGuid():N}";
        var seed = new ProjectState
        {
            ProjectId = "test-session",
            SolutionUniqueName = solutionUniqueName,
        };
        seed.AddPendingPlan(planId,
            "{ \"sitemapname\": \"Demo Sitemap\", \"sitemapnameunique\": \"eppc_demositemap\", \"sitemapxml\": \"<SiteMap />\" }");
        stored["test-session"] = seed;

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
        var tool = new ExecuteCreateSiteMapTool(client.Object, store.Object, ctx.Object,
            NullLogger<ExecuteCreateSiteMapTool>.Instance);
        return (tool, client, stored, planId);
    }

    [Fact]
    public async Task ExecuteCreateSiteMap_Happy_CreatesAndTracksInState()
    {
        var (tool, client, stored, planId) = BuildTool();
        var siteMapId = Guid.NewGuid();
        client.Setup(c => c.CreateSiteMapAsync(It.IsAny<string>(), "EPPCLiveDemo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(siteMapId);

        var result = await tool.ExecuteCreateSiteMapAsync(planId, "Demo Sitemap", CancellationToken.None);

        Assert.Contains($"SiteMapId={siteMapId}", result);
        Assert.Contains("Added to solution 'EPPCLiveDemo'", result);
        Assert.Contains("attach_sitemap_to_app", result);

        var state = stored["test-session"];
        Assert.False(state.PendingPlans.ContainsKey(planId));
        Assert.True(state.ExecutedPlans.ContainsKey(planId));
        Assert.Single(state.SiteMaps);
        Assert.Equal(siteMapId, state.SiteMaps[0].SiteMapId);
        Assert.Equal("eppc_demositemap", state.SiteMaps[0].SiteMapNameUnique);
        Assert.Null(state.SiteMaps[0].AttachedToAppId);   // not yet attached
    }

    [Fact]
    public async Task ExecuteCreateSiteMap_AlreadyExecuted_DoesNotRetry()
    {
        var (tool, client, stored, planId) = BuildTool();
        var state = stored["test-session"];
        state.ExecutedPlans[planId] = state.PendingPlans[planId];
        state.PendingPlans.Remove(planId);

        var result = await tool.ExecuteCreateSiteMapAsync(planId, "Demo Sitemap", CancellationToken.None);

        Assert.StartsWith("ALREADY_EXECUTED:", result);
        client.Verify(c => c.CreateSiteMapAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCreateSiteMap_RejectedPlanId_Skips()
    {
        var (tool, client, _, _) = BuildTool();

        var result = await tool.ExecuteCreateSiteMapAsync(
            $"rejected_create_sitemap_{Guid.NewGuid():N}", "Demo", CancellationToken.None);

        Assert.StartsWith("SKIPPED:", result);
        client.Verify(c => c.CreateSiteMapAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCreateSiteMap_PlanNotFound_ReturnsError()
    {
        var (tool, client, _, _) = BuildTool();

        var result = await tool.ExecuteCreateSiteMapAsync(
            $"create_sitemap_{Guid.NewGuid():N}", "Demo", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("not found", result);
        client.Verify(c => c.CreateSiteMapAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
