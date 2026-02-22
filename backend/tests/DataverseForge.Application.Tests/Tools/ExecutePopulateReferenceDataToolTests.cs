using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecutePopulateReferenceDataToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecutePopulateReferenceDataTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        var createdIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mockDataverseClient
            .Setup(c => c.CreateRecordsBatchAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchOperationResult(2, 0, createdIds.AsReadOnly(), new List<string>().AsReadOnly()));
        return state;
    }

    [Fact]
    public async Task CallsCreateRecordsBatchAsync()
    {
        var sessionId = "exec-pop-test-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var payloads = "[\"{ \\\"cr123_name\\\": \\\"Value1\\\" }\",\"{ \\\"cr123_name\\\": \\\"Value2\\\" }\"]";

        await tool.ExecutePopulateReferenceDataAsync(
            "cr123_categories", "cr123_category", payloads, CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateRecordsBatchAsync("cr123_categories", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddsToStateSeedDataRecords()
    {
        var sessionId = "exec-pop-test-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        var payloads = "[\"{ \\\"cr123_name\\\": \\\"Value1\\\" }\",\"{ \\\"cr123_name\\\": \\\"Value2\\\" }\"]";

        await tool.ExecutePopulateReferenceDataAsync(
            "cr123_categories", "cr123_category", payloads, CancellationToken.None);

        Assert.Equal(2, state.SeedDataRecords.Count);
        Assert.All(state.SeedDataRecords, r => Assert.Equal("cr123_category", r.TableLogicalName));
    }

    [Fact]
    public async Task PushesToOperationStack()
    {
        var sessionId = "exec-pop-test-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        var payloads = "[\"{ \\\"cr123_name\\\": \\\"Value1\\\" }\"]";

        await tool.ExecutePopulateReferenceDataAsync(
            "cr123_categories", "cr123_category", payloads, CancellationToken.None);

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
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var payloads = "[\"{ \\\"cr123_name\\\": \\\"Value1\\\" }\"]";

        await tool.ExecutePopulateReferenceDataAsync(
            "cr123_categories", "cr123_category", payloads, CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
