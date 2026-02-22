using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateModelDrivenAppToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateModelDrivenAppTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateModelDrivenAppAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return state;
    }

    [Fact]
    public async Task ExecuteCreateModelDrivenApp_CallsCreateAsync()
    {
        var sessionId = "exec-app-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateModelDrivenAppAsync("{}", "Field Service Hub", "cr123_fsh", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateModelDrivenAppAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateModelDrivenApp_AddsToStateModelDrivenApps()
    {
        var sessionId = "exec-app-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateModelDrivenAppAsync("{}", "Field Service Hub", "cr123_fsh", CancellationToken.None);

        Assert.Single(state.ModelDrivenApps);
        Assert.Equal("Field Service Hub", state.ModelDrivenApps[0].Name);
        Assert.Equal("cr123_fsh", state.ModelDrivenApps[0].AppUniqueName);
    }

    [Fact]
    public async Task ExecuteCreateModelDrivenApp_PushesToOperationStack()
    {
        var sessionId = "exec-app-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateModelDrivenAppAsync("{}", "Field Service Hub", "cr123_fsh", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateModelDrivenApp", op.ToolName);
        Assert.Equal(AgentNames.UIBuilder, op.AgentName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateModelDrivenApp_SavesState()
    {
        var sessionId = "exec-app-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateModelDrivenAppAsync("{}", "Field Service Hub", "cr123_fsh", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
