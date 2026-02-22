using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateViewToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateViewTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
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
    public async Task ExecuteCreateView_CallsCreateViewAsync()
    {
        var sessionId = "exec-view-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateViewAsync("{}", "cr123_workorder", "Active Work Orders", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateViewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateView_AddsToStateViews()
    {
        var sessionId = "exec-view-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateViewAsync("{}", "cr123_workorder", "Active Work Orders", CancellationToken.None);

        Assert.Single(state.Views);
        Assert.Equal("cr123_workorder", state.Views[0].TableLogicalName);
        Assert.Equal("Active Work Orders", state.Views[0].Name);
    }

    [Fact]
    public async Task ExecuteCreateView_PushesToOperationStack()
    {
        var sessionId = "exec-view-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateViewAsync("{}", "cr123_workorder", "Active Work Orders", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateView", op.ToolName);
        Assert.Equal(AgentNames.UIBuilder, op.AgentName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateView_SavesState()
    {
        var sessionId = "exec-view-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateViewAsync("{}", "cr123_workorder", "Active Work Orders", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
