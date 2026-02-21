using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateGlobalOptionSetToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateGlobalOptionSetTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateGlobalOptionSetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return state;
    }

    [Fact]
    public async Task ExecuteGlobalOptionSet_CallsCreateAndAddSolutionComponent()
    {
        var sessionId = "exec-gos-test-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateGlobalOptionSetAsync("{}", "cr123_priority", "TestSolution", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateGlobalOptionSetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(
            c => c.AddSolutionComponentAsync(It.IsAny<Guid>(), 9, "TestSolution", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteGlobalOptionSet_AddsToStateOptionSets()
    {
        var sessionId = "exec-gos-test-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateGlobalOptionSetAsync("{}", "cr123_priority", "TestSolution", CancellationToken.None);

        Assert.Single(state.OptionSets);
        Assert.Equal("cr123_priority", state.OptionSets[0].Name);
        Assert.True(state.OptionSets[0].IsGlobal);
    }

    [Fact]
    public async Task ExecuteGlobalOptionSet_PushesToOperationStack()
    {
        var sessionId = "exec-gos-test-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateGlobalOptionSetAsync("{}", "cr123_priority", "TestSolution", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateGlobalOptionSet", op.ToolName);
        Assert.True(op.IsReversible);
    }
}
