using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateAppToolTests
{
    private static (ExecuteCreateAppTool Tool, Mock<IDataverseClient> Client, Dictionary<string, ProjectState> Stored, string PlanId) BuildTool(
        string solutionUniqueName = "EPPCLiveDemo")
    {
        var stored = new Dictionary<string, ProjectState>();
        var planId = $"create_app_{Guid.NewGuid():N}";
        var seed = new ProjectState
        {
            ProjectId = "test-session",
            SolutionUniqueName = solutionUniqueName,
        };
        seed.AddPendingPlan(planId, "{ \"name\": \"Demo\", \"uniquename\": \"eppc_demo\" }");
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
        var tool = new ExecuteCreateAppTool(client.Object, store.Object, ctx.Object,
            NullLogger<ExecuteCreateAppTool>.Instance);
        return (tool, client, stored, planId);
    }

    [Fact]
    public async Task ExecuteCreateApp_HappyPath_CreatesPublishesAndTracksInState()
    {
        var (tool, client, stored, planId) = BuildTool(solutionUniqueName: "EPPCLiveDemo");
        var newAppId = Guid.NewGuid();

        client.Setup(c => c.CreateAppAsync(It.IsAny<string>(), "EPPCLiveDemo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAppId);
        client.Setup(c => c.PublishAppAsync(newAppId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await tool.ExecuteCreateAppAsync(planId, "Demo", CancellationToken.None);

        Assert.Contains($"AppModuleId={newAppId}", result);
        Assert.Contains("Added to solution 'EPPCLiveDemo'", result);
        Assert.Contains("Published", result);

        // Plan moved from Pending → Executed
        var state = stored["test-session"];
        Assert.False(state.PendingPlans.ContainsKey(planId));
        Assert.True(state.ExecutedPlans.ContainsKey(planId));

        // App tracked in state.Apps
        Assert.Single(state.Apps);
        Assert.Equal(newAppId, state.Apps[0].AppModuleId);
        Assert.Equal("Demo", state.Apps[0].Name);
        Assert.Equal("eppc_demo", state.Apps[0].UniqueName);

        // Operation pushed onto stack
        Assert.Single(state.OperationStack);
        Assert.Equal("ExecuteCreateApp", state.OperationStack.Peek().ToolName);

        client.Verify(c => c.CreateAppAsync(It.IsAny<string>(), "EPPCLiveDemo", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.PublishAppAsync(newAppId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateApp_PassesNullSolution_WhenNoActiveSolution()
    {
        var (tool, client, _, planId) = BuildTool(solutionUniqueName: "");
        var newAppId = Guid.NewGuid();

        client.Setup(c => c.CreateAppAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAppId);
        client.Setup(c => c.PublishAppAsync(newAppId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await tool.ExecuteCreateAppAsync(planId, "Demo", CancellationToken.None);

        Assert.Contains("WARNING: no active solution selected", result);
        client.Verify(c => c.CreateAppAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateApp_SkipsRejectedPlanId()
    {
        var (tool, client, _, _) = BuildTool();

        var result = await tool.ExecuteCreateAppAsync(
            $"rejected_create_app_{Guid.NewGuid():N}", "Demo", CancellationToken.None);

        Assert.StartsWith("SKIPPED:", result);
        client.Verify(c => c.CreateAppAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCreateApp_ReturnsAlreadyExecuted_WhenPlanAlreadyRan()
    {
        var (tool, client, stored, planId) = BuildTool();
        var state = stored["test-session"];
        state.ExecutedPlans[planId] = state.PendingPlans[planId];
        state.PendingPlans.Remove(planId);

        var result = await tool.ExecuteCreateAppAsync(planId, "Demo", CancellationToken.None);

        Assert.StartsWith("ALREADY_EXECUTED:", result);
        client.Verify(c => c.CreateAppAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCreateApp_ReturnsError_WhenPlanIdNotFound()
    {
        var (tool, client, _, _) = BuildTool();

        var result = await tool.ExecuteCreateAppAsync(
            $"create_app_{Guid.NewGuid():N}", "Demo", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("not found", result);
        client.Verify(c => c.CreateAppAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCreateApp_SurfacesDataverseErrorVerbatim()
    {
        var (tool, client, stored, planId) = BuildTool();

        client.Setup(c => c.CreateAppAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Create app (appmodule) failed: HTTP 400 — uniquename: An app with the same uniquename already exists."));

        var result = await tool.ExecuteCreateAppAsync(planId, "Demo", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("uniquename: An app with the same uniquename already exists", result);

        // Plan stays pending (not promoted to ExecutedPlans) and audit logs the failure.
        var state = stored["test-session"];
        Assert.True(state.PendingPlans.ContainsKey(planId));
        Assert.False(state.ExecutedPlans.ContainsKey(planId));
        Assert.Contains(state.AuditLog, e => e.Action == "ExecuteCreateApp" && !e.Success);
    }
}
