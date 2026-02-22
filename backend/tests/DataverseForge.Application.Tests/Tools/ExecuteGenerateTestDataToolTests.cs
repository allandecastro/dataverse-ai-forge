using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteGenerateTestDataToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteGenerateTestDataTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mockDataverseClient
            .Setup(c => c.CreateRecordsBatchAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchOperationResult(
                SuccessCount: 2,
                FailureCount: 0,
                CreatedRecordIds: [Guid.NewGuid(), Guid.NewGuid()],
                Errors: []));
        return state;
    }

    [Fact]
    public async Task CallsCreateRecordsBatchAsync()
    {
        var sessionId = "exec-testdata-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var recordPayloadsJson = """["{\"cr_name\":\"Record 1\"}","{\"cr_name\":\"Record 2\"}"]""";

        await tool.ExecuteGenerateTestDataAsync(
            "cr_workorders", "cr_workorder", recordPayloadsJson, CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateRecordsBatchAsync(
                "cr_workorders",
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddsToStateSeedDataRecords_WithTestDataTag()
    {
        var sessionId = "exec-testdata-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        var recordPayloadsJson = """["{\"cr_name\":\"Record 1\"}","{\"cr_name\":\"Record 2\"}"]""";

        await tool.ExecuteGenerateTestDataAsync(
            "cr_workorders", "cr_workorder", recordPayloadsJson, CancellationToken.None);

        Assert.Equal(2, state.SeedDataRecords.Count);
        Assert.All(state.SeedDataRecords, r =>
        {
            Assert.Equal("cr_workorder", r.TableLogicalName);
            Assert.Equal("[TEST DATA]", r.Description);
        });
    }

    [Fact]
    public async Task PushesToOperationStack()
    {
        var sessionId = "exec-testdata-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        var recordPayloadsJson = """["{\"cr_name\":\"Record 1\"}","{\"cr_name\":\"Record 2\"}"]""";

        await tool.ExecuteGenerateTestDataAsync(
            "cr_workorders", "cr_workorder", recordPayloadsJson, CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal(AgentNames.DataPopulation, op.AgentName);
        Assert.Equal("ExecuteGenerateTestData", op.ToolName);
        Assert.True(op.IsReversible);
        Assert.NotNull(op.ReverseOp);
    }

    [Fact]
    public async Task SavesState()
    {
        var sessionId = "exec-testdata-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var recordPayloadsJson = """["{\"cr_name\":\"Record 1\"}","{\"cr_name\":\"Record 2\"}"]""";

        await tool.ExecuteGenerateTestDataAsync(
            "cr_workorders", "cr_workorder", recordPayloadsJson, CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
