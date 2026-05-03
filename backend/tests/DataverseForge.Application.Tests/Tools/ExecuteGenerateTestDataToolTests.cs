using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteGenerateTestDataToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteGenerateTestDataTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteGenerateTestDataTool>.Instance);

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

    // The agent has historically passed three different shapes for recordPayloadsJson —
    // each used to throw uncaught and the framework masked it as "Function failed.".
    // These tests pin all three to the same successful path.

    [Fact]
    public async Task AcceptsArrayOfObjectsShape()
    {
        var sessionId = "exec-testdata-shape-objs";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        // Plain JSON array of record objects (the most natural thing for an LLM to write).
        var recordPayloadsJson = """[{"cr_name":"Record 1"},{"cr_name":"Record 2"}]""";

        var result = await tool.ExecuteGenerateTestDataAsync(
            "cr_workorders", "cr_workorder", recordPayloadsJson, CancellationToken.None);

        Assert.Equal(2, result.SuccessCount);
        _mockDataverseClient.Verify(
            c => c.CreateRecordsBatchAsync(
                "cr_workorders",
                It.Is<IReadOnlyList<string>>(l => l.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AcceptsFullBatchPayloadShape()
    {
        var sessionId = "exec-testdata-shape-batch";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        // The exact shape returned in TestDataPlan.WebApiBatchPayloadJson — agent
        // passing this verbatim used to crash with "Function failed.".
        var recordPayloadsJson = """{"entitySetName":"cr_workorders","records":["{\"cr_name\":\"Record 1\"}","{\"cr_name\":\"Record 2\"}"]}""";

        var result = await tool.ExecuteGenerateTestDataAsync(
            "cr_workorders", "cr_workorder", recordPayloadsJson, CancellationToken.None);

        Assert.Equal(2, result.SuccessCount);
    }

    [Fact]
    public async Task InvalidJson_ReturnsCleanErrorInsteadOfThrowing()
    {
        var sessionId = "exec-testdata-invalid";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.ExecuteGenerateTestDataAsync(
            "cr_workorders", "cr_workorder", "NOT_VALID_JSON", CancellationToken.None);

        // Used to throw JsonException → Microsoft.Extensions.AI swallowed and surfaced
        // as "Function failed." with no actionable info. Now returns a real error.
        Assert.Equal(0, result.SuccessCount);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("not valid JSON"));
    }
}
