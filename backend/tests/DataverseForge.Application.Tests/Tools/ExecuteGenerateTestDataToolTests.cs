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

    /// <summary>
    /// Seed a pending plan with the given <paramref name="batchPayloadJson"/>
    /// and stub <c>CreateRecordsBatchAsync</c> with a fixed success count. The
    /// executor looks up the planId in PendingPlans on every call.
    /// </summary>
    private (ProjectState state, string planId) Seed(string sessionId, string batchPayloadJson, int successCount = 2)
    {
        var state = new ProjectState { ProjectId = sessionId };
        var planId = $"create_test_data_{Guid.NewGuid():N}";
        state.AddPendingPlan(planId, batchPayloadJson);

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
                SuccessCount: successCount,
                FailureCount: 0,
                CreatedRecordIds: Enumerable.Range(0, successCount).Select(_ => Guid.NewGuid()).ToList(),
                Errors: []));
        return (state, planId);
    }

    [Fact]
    public async Task PlanIdResolvesToBatchPayload_AndCallsCreateRecordsBatch()
    {
        var batchPayload = """{"entitySetName":"cr_workorders","records":["{\"cr_name\":\"Record 1\"}","{\"cr_name\":\"Record 2\"}"]}""";
        var (_, planId) = Seed("exec-testdata-1", batchPayload);
        var tool = CreateTool();

        await tool.ExecuteGenerateTestDataAsync(planId, "cr_workorders", "cr_workorder", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateRecordsBatchAsync(
                "cr_workorders",
                It.Is<IReadOnlyList<string>>(l => l.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddsToStateSeedDataRecords_WithTestDataTag()
    {
        var batchPayload = """{"entitySetName":"cr_workorders","records":["{\"cr_name\":\"R1\"}","{\"cr_name\":\"R2\"}"]}""";
        var (state, planId) = Seed("exec-testdata-2", batchPayload);
        var tool = CreateTool();

        await tool.ExecuteGenerateTestDataAsync(planId, "cr_workorders", "cr_workorder", CancellationToken.None);

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
        var batchPayload = """{"entitySetName":"cr_workorders","records":["{\"cr_name\":\"R1\"}","{\"cr_name\":\"R2\"}"]}""";
        var (state, planId) = Seed("exec-testdata-3", batchPayload);
        var tool = CreateTool();

        await tool.ExecuteGenerateTestDataAsync(planId, "cr_workorders", "cr_workorder", CancellationToken.None);

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
        var batchPayload = """{"entitySetName":"cr_workorders","records":["{\"cr_name\":\"R1\"}"]}""";
        var (_, planId) = Seed("exec-testdata-4", batchPayload, successCount: 1);
        var tool = CreateTool();

        await tool.ExecuteGenerateTestDataAsync(planId, "cr_workorders", "cr_workorder", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync("exec-testdata-4", It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── planId-by-reference behavior ───

    [Fact]
    public async Task RejectedPlanId_SkipsAndReportsError()
    {
        var sessionId = "exec-testdata-rejected";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        var tool = CreateTool();

        var result = await tool.ExecuteGenerateTestDataAsync(
            $"rejected_test_data_invalid_{Guid.NewGuid():N}",
            "cr_workorders", "cr_workorder", CancellationToken.None);

        Assert.Contains(result.Errors, e => e.Contains("SKIPPED", StringComparison.OrdinalIgnoreCase));
        _mockDataverseClient.Verify(
            c => c.CreateRecordsBatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UnknownPlanId_ReturnsNotFound()
    {
        var sessionId = "exec-testdata-notfound";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        var tool = CreateTool();

        var result = await tool.ExecuteGenerateTestDataAsync(
            $"create_test_data_{Guid.NewGuid():N}",   // never added
            "cr_workorders", "cr_workorder", CancellationToken.None);

        Assert.Contains(result.Errors, e => e.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuccessfulRun_MovesPlanToExecutedAndDropsFromPending()
    {
        var batchPayload = """{"entitySetName":"cr_workorders","records":["{\"cr_name\":\"R1\"}"]}""";
        var (state, planId) = Seed("exec-testdata-moved", batchPayload, successCount: 1);
        var tool = CreateTool();

        await tool.ExecuteGenerateTestDataAsync(planId, "cr_workorders", "cr_workorder", CancellationToken.None);

        Assert.False(state.PendingPlans.ContainsKey(planId));
        Assert.True(state.ExecutedPlans.ContainsKey(planId));
    }

    [Fact]
    public async Task AlreadyExecuted_RefusesToRetry()
    {
        var sessionId = "exec-testdata-already";
        var state = new ProjectState { ProjectId = sessionId };
        var planId = $"create_test_data_{Guid.NewGuid():N}";
        state.ExecutedPlans[planId] = "{\"records\":[]}";
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        var tool = CreateTool();

        var result = await tool.ExecuteGenerateTestDataAsync(planId, "cr_workorders", "cr_workorder", CancellationToken.None);

        Assert.Contains(result.Errors, e => e.Contains("ALREADY_EXECUTED", StringComparison.Ordinal));
        _mockDataverseClient.Verify(
            c => c.CreateRecordsBatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── Back-compat: stored payload shape tolerance ───
    // Plans saved before this migration may use one of three shapes — the
    // executor's NormalizeRecordPayloads keeps accepting all of them so the
    // first session after the upgrade doesn't fail on existing PendingPlans.

    [Fact]
    public async Task StoredPayload_PlainArrayOfObjects_Accepted()
    {
        var (_, planId) = Seed("exec-testdata-shape-objs", """[{"cr_name":"R1"},{"cr_name":"R2"}]""");
        var tool = CreateTool();

        var result = await tool.ExecuteGenerateTestDataAsync(planId, "cr_workorders", "cr_workorder", CancellationToken.None);

        Assert.Equal(2, result.SuccessCount);
    }

    [Fact]
    public async Task StoredPayload_ArrayOfStringifiedRecords_Accepted()
    {
        var (_, planId) = Seed("exec-testdata-shape-strs", """["{\"cr_name\":\"R1\"}","{\"cr_name\":\"R2\"}"]""");
        var tool = CreateTool();

        var result = await tool.ExecuteGenerateTestDataAsync(planId, "cr_workorders", "cr_workorder", CancellationToken.None);

        Assert.Equal(2, result.SuccessCount);
    }
}
