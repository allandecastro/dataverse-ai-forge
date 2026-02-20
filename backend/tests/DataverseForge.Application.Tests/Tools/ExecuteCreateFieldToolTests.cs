using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateFieldToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateFieldTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateFieldAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return state;
    }

    [Fact]
    public async Task ExecuteCreateField_CallsCreateField()
    {
        var sessionId = "exec-field-test-1";
        SetupDefaults(sessionId);
        var tableMetadataId = Guid.NewGuid();
        var tool = CreateTool();

        await tool.ExecuteCreateFieldAsync(
            tableMetadataId.ToString(), "cr123_workorder", "{}",
            "cr123_priority", "Priority", "localOptionSet", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateFieldAsync(tableMetadataId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateField_AddsToStateFields()
    {
        var sessionId = "exec-field-test-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateFieldAsync(
            Guid.NewGuid().ToString(), "cr123_workorder", "{}",
            "cr123_priority", "Priority", "localOptionSet", CancellationToken.None);

        Assert.Single(state.Fields);
        Assert.Equal("cr123_priority", state.Fields[0].LogicalName);
        Assert.Equal("cr123_workorder", state.Fields[0].TableLogicalName);
        Assert.Equal("localOptionSet", state.Fields[0].FieldType);
    }

    [Fact]
    public async Task ExecuteCreateField_PushesToOperationStack()
    {
        var sessionId = "exec-field-test-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateFieldAsync(
            Guid.NewGuid().ToString(), "cr123_workorder", "{}",
            "cr123_priority", "Priority", "localOptionSet", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateField", op.ToolName);
        Assert.True(op.IsReversible);
    }
}
