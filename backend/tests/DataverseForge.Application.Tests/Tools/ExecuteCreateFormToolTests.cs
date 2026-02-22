using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateFormToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateFormTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateFormAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return state;
    }

    [Fact]
    public async Task ExecuteCreateForm_CallsCreateFormAsync()
    {
        var sessionId = "exec-form-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateFormAsync("{}", "cr123_workorder", "WO Main Form", "main", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateFormAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateForm_AddsToStateForms()
    {
        var sessionId = "exec-form-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateFormAsync("{}", "cr123_workorder", "WO Main Form", "main", CancellationToken.None);

        Assert.Single(state.Forms);
        Assert.Equal("cr123_workorder", state.Forms[0].TableLogicalName);
        Assert.Equal("WO Main Form", state.Forms[0].Name);
        Assert.Equal("main", state.Forms[0].FormType);
    }

    [Fact]
    public async Task ExecuteCreateForm_PushesToOperationStack()
    {
        var sessionId = "exec-form-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateFormAsync("{}", "cr123_workorder", "WO Main Form", "main", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateForm", op.ToolName);
        Assert.Equal(AgentNames.UIBuilder, op.AgentName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateForm_SavesState()
    {
        var sessionId = "exec-form-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateFormAsync("{}", "cr123_workorder", "WO Main Form", "main", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
