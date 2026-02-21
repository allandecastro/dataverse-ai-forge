using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteModifyFieldToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteModifyFieldTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.ModifyFieldAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return state;
    }

    [Fact]
    public async Task ExecuteModifyField_CallsModifyField()
    {
        var sessionId = "exec-modfld-test-1";
        SetupDefaults(sessionId);
        var tableId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var tool = CreateTool();

        await tool.ExecuteModifyFieldAsync(
            tableId.ToString(), fieldId.ToString(), "cr123_project", "cr123_name", "{}", "displayName", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.ModifyFieldAsync(tableId, fieldId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteModifyField_PushesToOperationStack()
    {
        var sessionId = "exec-modfld-test-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteModifyFieldAsync(
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "cr123_project", "cr123_name", "{}", "displayName", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteModifyField", op.ToolName);
        Assert.False(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteModifyField_ReturnsConfirmationMessage()
    {
        var sessionId = "exec-modfld-test-3";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.ExecuteModifyFieldAsync(
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "cr123_project", "cr123_name", "{}", "displayName, requiredLevel", CancellationToken.None);

        Assert.Contains("cr123_name", result);
        Assert.Contains("displayName, requiredLevel", result);
    }
}
