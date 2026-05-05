using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecutePopulateReferenceDataToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecutePopulateReferenceDataTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecutePopulateReferenceDataTool>.Instance);

    private (ProjectState state, string planId) SetupDefaults(string sessionId, int recordCount = 2)
    {
        var state = new ProjectState { ProjectId = sessionId };
        var createdIds = Enumerable.Range(0, recordCount).Select(_ => Guid.NewGuid()).ToList();

        // Seed a pending plan: the executor looks this up by planId.
        var planId = $"populate_reference_data_{Guid.NewGuid():N}";
        var records = Enumerable.Range(0, recordCount)
            .Select(i => $"{{\"cr123_name\":\"Value{i + 1}\"}}")
            .ToList();
        var batchPayload = System.Text.Json.JsonSerializer.Serialize(new { entitySetName = "cr123_categories", records });
        state.AddPendingPlan(planId, batchPayload);

        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mockDataverseClient
            .Setup(c => c.CreateRecordsBatchAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchOperationResult(recordCount, 0, createdIds.AsReadOnly(), new List<string>().AsReadOnly()));
        return (state, planId);
    }

    [Fact]
    public async Task CallsCreateRecordsBatchAsync_WithRecordsFromPlan()
    {
        var sessionId = "exec-pop-test-1";
        var (_, planId) = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecutePopulateReferenceDataAsync(
            planId, "cr123_categories", "cr123_category", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateRecordsBatchAsync("cr123_categories", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddsToStateSeedDataRecords()
    {
        var sessionId = "exec-pop-test-2";
        var (state, planId) = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecutePopulateReferenceDataAsync(
            planId, "cr123_categories", "cr123_category", CancellationToken.None);

        Assert.Equal(2, state.SeedDataRecords.Count);
        Assert.All(state.SeedDataRecords, r => Assert.Equal("cr123_category", r.TableLogicalName));
    }

    [Fact]
    public async Task PushesToOperationStack()
    {
        var sessionId = "exec-pop-test-3";
        var (state, planId) = SetupDefaults(sessionId, recordCount: 1);
        var tool = CreateTool();

        await tool.ExecutePopulateReferenceDataAsync(
            planId, "cr123_categories", "cr123_category", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal(AgentNames.DataPopulation, op.AgentName);
        Assert.Equal("ExecutePopulateReferenceData", op.ToolName);
        Assert.True(op.IsReversible);
        Assert.NotNull(op.ReverseOp);
    }

    [Fact]
    public async Task SavesState()
    {
        var sessionId = "exec-pop-test-4";
        var (_, planId) = SetupDefaults(sessionId, recordCount: 1);
        var tool = CreateTool();

        await tool.ExecutePopulateReferenceDataAsync(
            planId, "cr123_categories", "cr123_category", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── planId-by-reference behavior ───

    [Fact]
    public async Task RejectedPlanId_SkipsAndReportsError()
    {
        var sessionId = "exec-pop-test-rejected";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        var tool = CreateTool();

        var result = await tool.ExecutePopulateReferenceDataAsync(
            $"rejected_reference_data_invalid_{Guid.NewGuid():N}",
            "cr123_categories", "cr123_category", CancellationToken.None);

        Assert.Contains(result.Errors, e => e.Contains("SKIPPED", StringComparison.OrdinalIgnoreCase));
        _mockDataverseClient.Verify(
            c => c.CreateRecordsBatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UnknownPlanId_ReturnsNotFoundError()
    {
        var sessionId = "exec-pop-test-notfound";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        var tool = CreateTool();

        var result = await tool.ExecutePopulateReferenceDataAsync(
            $"populate_reference_data_{Guid.NewGuid():N}",  // never added
            "cr123_categories", "cr123_category", CancellationToken.None);

        Assert.Contains(result.Errors, e => e.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuccessfulRun_MovesPlanToExecutedAndDropsFromPending()
    {
        var sessionId = "exec-pop-test-moved";
        var (state, planId) = SetupDefaults(sessionId, recordCount: 1);
        var tool = CreateTool();

        await tool.ExecutePopulateReferenceDataAsync(
            planId, "cr123_categories", "cr123_category", CancellationToken.None);

        Assert.False(state.PendingPlans.ContainsKey(planId));
        Assert.True(state.ExecutedPlans.ContainsKey(planId));
    }
}
