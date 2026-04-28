using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateTableToolTests
{
    private const string PlanId = "create_table_test";
    private const string PayloadJson = "{\"SchemaName\":\"cr123_WorkOrder\"}";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateTableTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateTableTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.PendingPlans[PlanId] = PayloadJson;

        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var metadataId = Guid.NewGuid();
        _mockDataverseClient
            .Setup(c => c.CreateTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadataId);

        return state;
    }

    [Fact]
    public async Task ExecuteCreateTable_CallsCreateTableAndAddSolutionComponent()
    {
        SetupDefaults("exec-table-test-1");
        var tool = CreateTool();

        await tool.ExecuteCreateTableAsync(
            PlanId, "cr123_workorder", "Work Order", "TestSolution", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateTableAsync(PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(
            c => c.AddSolutionComponentAsync(It.IsAny<Guid>(), 1, "TestSolution", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateTable_AddsToStateTables_AndRemovesPlan()
    {
        var state = SetupDefaults("exec-table-test-2");
        var tool = CreateTool();

        await tool.ExecuteCreateTableAsync(
            PlanId, "cr123_workorder", "Work Order", "TestSolution", CancellationToken.None);

        Assert.Single(state.Tables);
        Assert.Equal("cr123_workorder", state.Tables[0].LogicalName);
        Assert.Equal("Work Order", state.Tables[0].DisplayName);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
    }

    [Fact]
    public async Task ExecuteCreateTable_PushesToOperationStack()
    {
        var state = SetupDefaults("exec-table-test-3");
        var tool = CreateTool();

        await tool.ExecuteCreateTableAsync(
            PlanId, "cr123_workorder", "Work Order", "TestSolution", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateTable", op.ToolName);
        Assert.True(op.IsReversible);
        Assert.NotNull(op.ReverseOp);
    }

    [Fact]
    public async Task ExecuteCreateTable_SavesState()
    {
        var sessionId = "exec-table-test-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateTableAsync(
            PlanId, "cr123_workorder", "Work Order", "TestSolution", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateTable_ReturnsError_WhenPlanIdMissing()
    {
        var state = SetupDefaults("exec-table-test-5");
        state.PendingPlans.Clear();
        var tool = CreateTool();

        var result = await tool.ExecuteCreateTableAsync(
            "missing_plan", "cr123_x", "X", "TestSolution", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("missing_plan", result);
        _mockDataverseClient.Verify(
            c => c.CreateTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
