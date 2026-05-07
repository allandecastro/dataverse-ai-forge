using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class AttachSiteMapToAppToolTests
{
    private static (AttachSiteMapToAppTool Tool, Mock<IDataverseClient> Client, Dictionary<string, ProjectState> Stored, Guid AppId, Guid SiteMapId) BuildTool()
    {
        var stored = new Dictionary<string, ProjectState>();
        var appId = Guid.NewGuid();
        var siteMapId = Guid.NewGuid();
        var seed = new ProjectState { ProjectId = "test-session" };
        seed.SiteMaps.Add(new CreatedSiteMap(siteMapId, "eppc_demosm", AttachedToAppId: null, DateTime.UtcNow));
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
        var tool = new AttachSiteMapToAppTool(client.Object, store.Object, ctx.Object,
            NullLogger<AttachSiteMapToAppTool>.Instance);
        return (tool, client, stored, appId, siteMapId);
    }

    [Fact]
    public async Task AttachSiteMap_Happy_PassesSitemapKindToAddAppComponents()
    {
        var (tool, client, stored, appId, siteMapId) = BuildTool();
        IReadOnlyList<AppComponentRef>? captured = null;
        client.Setup(c => c.AddAppComponentsAsync(appId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<AppComponentRef> comps, CancellationToken _) =>
            {
                captured = comps;
                return comps.Select(r => new AppComponentRefOutcome(r, true, null)).ToList().AsReadOnly();
            });

        var result = await tool.AttachSiteMapToAppAsync(appId.ToString(), siteMapId.ToString(), CancellationToken.None);

        Assert.Contains($"Sitemap {siteMapId} attached to app {appId}", result);
        Assert.NotNull(captured);
        Assert.Single(captured!);
        Assert.Equal("sitemap", captured![0].Kind);
        Assert.Equal(siteMapId, captured[0].Id);

        // State updated with attached app id
        var sm = stored["test-session"].SiteMaps.Single();
        Assert.Equal(appId, sm.AttachedToAppId);
    }

    [Fact]
    public async Task AttachSiteMap_RejectsInvalidGuid()
    {
        var (tool, client, _, _, siteMapId) = BuildTool();

        var result = await tool.AttachSiteMapToAppAsync("not-a-guid", siteMapId.ToString(), CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        client.Verify(c => c.AddAppComponentsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AttachSiteMap_SurfacesDataverseErrorVerbatim()
    {
        var (tool, client, _, appId, siteMapId) = BuildTool();
        client.Setup(c => c.AddAppComponentsAsync(appId, It.IsAny<IReadOnlyList<AppComponentRef>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("AddAppComponents failed: HTTP 400 — Component is already part of the app."));

        var result = await tool.AttachSiteMapToAppAsync(appId.ToString(), siteMapId.ToString(), CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("Component is already part of the app", result);
    }
}
