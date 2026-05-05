using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifySiteMapAgainstPlanToolTests
{
    private static (VerifySiteMapAgainstPlanTool Tool, Mock<IDataverseClient> Client, Dictionary<string, ProjectState> Stored, string PlanId, Guid AppId) BuildTool(string plannedXml)
    {
        var stored = new Dictionary<string, ProjectState>();
        var planId = $"create_sitemap_{Guid.NewGuid():N}";
        var appId = Guid.NewGuid();
        var payload = $"{{ \"sitemapxml\": {System.Text.Json.JsonSerializer.Serialize(plannedXml)} }}";
        var seed = new ProjectState { ProjectId = "test-session" };
        seed.ExecutedPlans[planId] = payload;
        stored["test-session"] = seed;

        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState(); stored[id] = s; }
                return s;
            });
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");
        var client = new Mock<IDataverseClient>();
        return (new VerifySiteMapAgainstPlanTool(client.Object, store.Object, ctx.Object), client, stored, planId, appId);
    }

    [Fact]
    public async Task Verify_Pass_WhenLiveXmlMatchesPlan()
    {
        var planned = "<SiteMap><Area Id=\"a\" ShowGroups=\"true\" /></SiteMap>";
        var (tool, client, _, planId, appId) = BuildTool(planned);
        client.Setup(c => c.GetSiteMapForAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiteMapInfo(Guid.NewGuid(), "n", "u", planned, false));

        var result = await tool.VerifySiteMapAgainstPlanAsync(planId, appId.ToString(), CancellationToken.None);

        Assert.Equal("PASS", result.Verdict);
        Assert.Empty(result.Discrepancies);
    }

    [Fact]
    public async Task Verify_PassUnderWhitespaceNormalization()
    {
        // Live XML may differ in whitespace formatting after Dataverse round-trips it.
        var planned = "<SiteMap><Area Id=\"a\" ShowGroups=\"true\" /></SiteMap>";
        var liveExtraWhitespace = "<SiteMap>\n  <Area Id=\"a\"\n        ShowGroups=\"true\" />\n</SiteMap>";
        var (tool, client, _, planId, appId) = BuildTool(planned);
        client.Setup(c => c.GetSiteMapForAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiteMapInfo(Guid.NewGuid(), "n", "u", liveExtraWhitespace, false));

        var result = await tool.VerifySiteMapAgainstPlanAsync(planId, appId.ToString(), CancellationToken.None);

        Assert.Equal("PASS", result.Verdict);
    }

    [Fact]
    public async Task Verify_FailWhenLiveXmlDiffers()
    {
        var planned = "<SiteMap><Area Id=\"a\" ShowGroups=\"true\" /></SiteMap>";
        var live = "<SiteMap><Area Id=\"different\" ShowGroups=\"true\" /></SiteMap>";
        var (tool, client, _, planId, appId) = BuildTool(planned);
        client.Setup(c => c.GetSiteMapForAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiteMapInfo(Guid.NewGuid(), "n", "u", live, false));

        var result = await tool.VerifySiteMapAgainstPlanAsync(planId, appId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Contains("sitemapxml differs"));
    }

    [Fact]
    public async Task Verify_FailWhenAppHasNoSitemapAttached()
    {
        var (tool, client, _, planId, appId) = BuildTool("<SiteMap />");
        client.Setup(c => c.GetSiteMapForAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SiteMapInfo?)null);

        var result = await tool.VerifySiteMapAgainstPlanAsync(planId, appId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Contains("attach_sitemap_to_app"));
    }

    [Fact]
    public async Task Verify_RejectsWrongPlanType()
    {
        var (tool, _, _, _, appId) = BuildTool("<SiteMap />");

        var result = await tool.VerifySiteMapAgainstPlanAsync(
            $"create_app_{Guid.NewGuid():N}", appId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Contains("WRONG PLAN TYPE"));
    }
}
