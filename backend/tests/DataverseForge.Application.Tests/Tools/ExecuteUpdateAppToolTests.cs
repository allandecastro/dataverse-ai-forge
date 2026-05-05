using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteUpdateAppToolTests
{
    private static (ExecuteUpdateAppTool Tool, Mock<IDataverseClient> Client, Dictionary<string, ProjectState> Stored, string PlanId, Guid AppId) BuildTool()
    {
        var stored = new Dictionary<string, ProjectState>();
        var planId = $"update_app_{Guid.NewGuid():N}";
        var appId = Guid.NewGuid();
        var seed = new ProjectState { ProjectId = "test-session" };
        seed.AddPendingPlan(planId, "{ \"name\": \"Demo Updated\" }");
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
        var tool = new ExecuteUpdateAppTool(client.Object, store.Object, ctx.Object,
            NullLogger<ExecuteUpdateAppTool>.Instance);
        return (tool, client, stored, planId, appId);
    }

    [Fact]
    public async Task ExecuteUpdateApp_HappyPath_PatchesAndPublishes()
    {
        var (tool, client, stored, planId, appId) = BuildTool();
        client.Setup(c => c.UpdateAppAsync(appId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        client.Setup(c => c.PublishAppAsync(appId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await tool.ExecuteUpdateAppAsync(planId, appId.ToString(), CancellationToken.None);

        Assert.Contains($"App id={appId} updated", result);
        Assert.Contains("Published", result);

        var state = stored["test-session"];
        Assert.True(state.ExecutedPlans.ContainsKey(planId));
        Assert.False(state.PendingPlans.ContainsKey(planId));

        client.Verify(c => c.UpdateAppAsync(appId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.PublishAppAsync(appId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteUpdateApp_ReturnsError_OnInvalidGuid()
    {
        var (tool, client, _, planId, _) = BuildTool();

        var result = await tool.ExecuteUpdateAppAsync(planId, "not-a-guid", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        client.Verify(c => c.UpdateAppAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteUpdateApp_ReturnsAlreadyExecuted_WhenPlanAlreadyRan()
    {
        var (tool, client, stored, planId, appId) = BuildTool();
        var state = stored["test-session"];
        state.ExecutedPlans[planId] = state.PendingPlans[planId];
        state.PendingPlans.Remove(planId);

        var result = await tool.ExecuteUpdateAppAsync(planId, appId.ToString(), CancellationToken.None);

        Assert.StartsWith("ALREADY_EXECUTED:", result);
        client.Verify(c => c.UpdateAppAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
