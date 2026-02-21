using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteAddOptionToOptionSetToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteAddOptionToOptionSetTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.InsertOptionValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return state;
    }

    [Fact]
    public async Task ExecuteAddOption_CallsInsertOptionValue()
    {
        var sessionId = "exec-addopt-test-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteAddOptionToOptionSetAsync("{}", "cr123_priority", true, 100003, "Critical", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.InsertOptionValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAddOption_PushesToOperationStack()
    {
        var sessionId = "exec-addopt-test-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteAddOptionToOptionSetAsync("{}", "cr123_priority", true, 100003, "Critical", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteAddOptionToOptionSet", op.ToolName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteAddOption_ReturnsConfirmationMessage()
    {
        var sessionId = "exec-addopt-test-3";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.ExecuteAddOptionToOptionSetAsync("{}", "cr123_priority", true, 100003, "Critical", CancellationToken.None);

        Assert.Contains("Critical", result);
        Assert.Contains("100003", result);
    }
}
