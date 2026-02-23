using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateDashboardToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateDashboardTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateDashboardTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateDashboardAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return state;
    }

    [Fact]
    public async Task ExecuteCreateDashboard_CallsCreateDashboardAsync()
    {
        var sessionId = "exec-dash-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateDashboardAsync("{}", "Work Order Overview", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateDashboardAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateDashboard_AddsToStateDashboards()
    {
        var sessionId = "exec-dash-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateDashboardAsync("{}", "Work Order Overview", CancellationToken.None);

        Assert.Single(state.Dashboards);
        Assert.Equal("Work Order Overview", state.Dashboards[0].Name);
    }

    [Fact]
    public async Task ExecuteCreateDashboard_PushesToOperationStack()
    {
        var sessionId = "exec-dash-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateDashboardAsync("{}", "Work Order Overview", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateDashboard", op.ToolName);
        Assert.Equal(AgentNames.UIBuilder, op.AgentName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateDashboard_SavesState()
    {
        var sessionId = "exec-dash-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateDashboardAsync("{}", "Work Order Overview", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
