using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateViewToolTests
{
    private const string PlanId = "create_view_test";
    private const string PayloadJson = "{\"name\":\"Active Work Orders\"}";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateViewTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateViewTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.PendingPlans[PlanId] = PayloadJson;
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateViewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return state;
    }

    [Fact]
    public async Task ExecuteCreateView_LooksUpPayloadByPlanId_AndCallsCreate()
    {
        SetupDefaults("exec-view-1");
        var tool = CreateTool();

        await tool.ExecuteCreateViewAsync(PlanId, "cr123_workorder", "Active Work Orders", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateViewAsync(PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateView_AddsToStateViews_AndRemovesPlan()
    {
        var state = SetupDefaults("exec-view-2");
        var tool = CreateTool();

        await tool.ExecuteCreateViewAsync(PlanId, "cr123_workorder", "Active Work Orders", CancellationToken.None);

        Assert.Single(state.Views);
        Assert.Equal("cr123_workorder", state.Views[0].TableLogicalName);
        Assert.Equal("Active Work Orders", state.Views[0].Name);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
    }

    [Fact]
    public async Task ExecuteCreateView_PushesToOperationStack()
    {
        var state = SetupDefaults("exec-view-3");
        var tool = CreateTool();

        await tool.ExecuteCreateViewAsync(PlanId, "cr123_workorder", "Active Work Orders", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateView", op.ToolName);
        Assert.Equal(AgentNames.UIBuilder, op.AgentName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateView_ReturnsError_WhenPlanIdMissing()
    {
        var state = SetupDefaults("exec-view-4");
        state.PendingPlans.Clear();
        var tool = CreateTool();

        var result = await tool.ExecuteCreateViewAsync("missing", "cr123_workorder", "Active Work Orders", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _mockDataverseClient.Verify(
            c => c.CreateViewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
