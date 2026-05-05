using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifyAppAgainstPlanToolTests
{
    static VerifyAppAgainstPlanToolTests()
    {
        // Zero out the retry backoff so tests don't actually wait 4 seconds when
        // the verifier retries on null. Production keeps the full schedule.
        VerifyAppAgainstPlanTool.RetryDelaysMs = [0, 0, 0];
    }

    private static (VerifyAppAgainstPlanTool Tool, Mock<IDataverseClient> Client, Dictionary<string, ProjectState> Stored, string PlanId, Guid AppId) BuildTool(string plannedPayload)
    {
        var stored = new Dictionary<string, ProjectState>();
        var planId = $"create_app_{Guid.NewGuid():N}";
        var appId = Guid.NewGuid();
        var seed = new ProjectState { ProjectId = "test-session" };
        seed.ExecutedPlans[planId] = plannedPayload;
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
        return (new VerifyAppAgainstPlanTool(client.Object, store.Object, ctx.Object), client, stored, planId, appId);
    }

    [Fact]
    public async Task VerifyApp_ReturnsPass_WhenAllFieldsMatch()
    {
        var payload = "{ \"name\": \"Demo\", \"uniquename\": \"eppc_demo\", \"description\": \"d\", \"clienttype\": 4, \"navigationtype\": 0 }";
        var (tool, client, _, planId, appId) = BuildTool(payload);

        client.Setup(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppModuleDetails(
                appId, "Demo", "eppc_demo", "d", "", 4, 0, 0, 1, false, null, null, null));

        var result = await tool.VerifyAppAgainstPlanAsync(planId, appId.ToString(), CancellationToken.None);

        Assert.Equal("PASS", result.Verdict);
        Assert.Empty(result.Discrepancies);
    }

    [Fact]
    public async Task VerifyApp_ReturnsFail_OnNameMismatch()
    {
        var payload = "{ \"name\": \"Demo\", \"uniquename\": \"eppc_demo\", \"description\": \"d\", \"clienttype\": 4, \"navigationtype\": 0 }";
        var (tool, client, _, planId, appId) = BuildTool(payload);

        client.Setup(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppModuleDetails(
                appId, "WrongName", "eppc_demo", "d", "", 4, 0, 0, 1, false, null, null, null));

        var result = await tool.VerifyAppAgainstPlanAsync(planId, appId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.StartsWith("name:"));
    }

    [Fact]
    public async Task VerifyApp_ReturnsPartial_OnDescriptionDriftOnly()
    {
        var payload = "{ \"name\": \"Demo\", \"uniquename\": \"eppc_demo\", \"description\": \"planned\", \"clienttype\": 4, \"navigationtype\": 0 }";
        var (tool, client, _, planId, appId) = BuildTool(payload);

        client.Setup(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppModuleDetails(
                appId, "Demo", "eppc_demo", "drifted", "", 4, 0, 0, 1, false, null, null, null));

        var result = await tool.VerifyAppAgainstPlanAsync(planId, appId.ToString(), CancellationToken.None);

        Assert.Equal("PARTIAL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.StartsWith("description:"));
    }

    [Fact]
    public async Task VerifyApp_RejectsWrongPlanType()
    {
        var (tool, _, _, _, appId) = BuildTool("{}");

        var result = await tool.VerifyAppAgainstPlanAsync(
            $"create_dashboard_{Guid.NewGuid():N}", appId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Contains("WRONG PLAN TYPE"));
    }

    [Fact]
    public async Task VerifyApp_RejectsInvalidGuid()
    {
        var (tool, _, _, planId, _) = BuildTool("{}");

        var result = await tool.VerifyAppAgainstPlanAsync(planId, "not-a-guid", CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Contains("not a valid GUID"));
    }

    [Fact]
    public async Task VerifyApp_RetriesGetAppAsync_UntilAppShowsUp()
    {
        // Real session 2026-05-05: a freshly-created appmodule returned 404
        // from GET /appmodules({id}) for several seconds after create succeeded.
        // The verifier must retry rather than report FAIL on the first null.
        var payload = "{ \"name\": \"Demo\", \"uniquename\": \"eppc_demo\", \"description\": \"\", \"clienttype\": 4, \"navigationtype\": 0 }";
        var (tool, client, _, planId, appId) = BuildTool(payload);

        // First two calls return null (eventual consistency); third returns the app.
        var sequence = new Queue<AppModuleDetails?>();
        sequence.Enqueue(null);
        sequence.Enqueue(null);
        sequence.Enqueue(new AppModuleDetails(appId, "Demo", "eppc_demo", "", "", 4, 0, 0, 1, false, null, null, null));

        client.Setup(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => sequence.Dequeue());

        var result = await tool.VerifyAppAgainstPlanAsync(planId, appId.ToString(), CancellationToken.None);

        Assert.Equal("PASS", result.Verdict);
        client.Verify(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task VerifyApp_FailsAfterRetriesExhausted_WithActionableMessage()
    {
        var (tool, client, _, planId, appId) = BuildTool("{}");
        client.Setup(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppModuleDetails?)null);

        var result = await tool.VerifyAppAgainstPlanAsync(planId, appId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        // Message should hint that this might be a false-FAIL ("check maker portal").
        Assert.Contains(result.Discrepancies, d => d.Contains("eventual consistency", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Discrepancies, d => d.Contains("maker portal", StringComparison.OrdinalIgnoreCase));
        // 3 attempts were made (0ms, 1s, 3s).
        client.Verify(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task VerifyApp_FailsWhenPlanNotInExecutedPlans()
    {
        var (tool, client, stored, _, appId) = BuildTool("{}");
        // Use a planId that's NOT in ExecutedPlans
        var orphanPlanId = $"create_app_{Guid.NewGuid():N}";

        client.Setup(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppModuleDetails(
                appId, "X", "eppc_x", "", "", 4, 0, 0, 1, false, null, null, null));

        var result = await tool.VerifyAppAgainstPlanAsync(orphanPlanId, appId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Contains("not found in ExecutedPlans"));
    }
}
