using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateGlobalOptionSetToolTests
{
    private const string PlanId = "create_global_option_set_test";
    private const string PayloadJson = "{\"@odata.type\":\"Microsoft.Dynamics.CRM.OptionSetMetadata\",\"Name\":\"cr123_priority\"}";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateGlobalOptionSetTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateGlobalOptionSetTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.PendingPlans[PlanId] = PayloadJson;
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
    public async Task ExecuteGlobalOptionSet_LooksUpPayloadByPlanId_AndCallsCreate()
    {
        SetupDefaults("exec-gos-test-1");
        var tool = CreateTool();

        await tool.ExecuteCreateGlobalOptionSetAsync(PlanId, "cr123_priority", "TestSolution", CancellationToken.None);

        // The executor must look up the stored payload by planId — not accept it as an argument.
        _mockDataverseClient.Verify(
            c => c.CreateGlobalOptionSetAsync(PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(
            c => c.AddSolutionComponentAsync(It.IsAny<Guid>(), SolutionComponentType.OptionSet, "TestSolution", It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteGlobalOptionSet_AddsToStateOptionSets_AndRemovesPlan()
    {
        var state = SetupDefaults("exec-gos-test-2");
        var tool = CreateTool();

        await tool.ExecuteCreateGlobalOptionSetAsync(PlanId, "cr123_priority", "TestSolution", CancellationToken.None);

        Assert.Single(state.OptionSets);
        Assert.Equal("cr123_priority", state.OptionSets[0].Name);
        Assert.True(state.OptionSets[0].IsGlobal);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
    }

    [Fact]
    public async Task ExecuteGlobalOptionSet_PushesToOperationStack()
    {
        var state = SetupDefaults("exec-gos-test-3");
        var tool = CreateTool();

        await tool.ExecuteCreateGlobalOptionSetAsync(PlanId, "cr123_priority", "TestSolution", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateGlobalOptionSet", op.ToolName);
        // F-25 (shipped): IsReversible=true now that UndoLastOperationTool has
        // a DeleteGlobalOptionSet case AND IDataverseClient ships
        // DeleteGlobalOptionSetAsync.
        Assert.True(op.IsReversible);
        Assert.NotNull(op.ReverseOp);
        Assert.Equal("DeleteGlobalOptionSet", op.ReverseOp.ToolName);
    }

    [Fact]
    public async Task ExecuteGlobalOptionSet_ReturnsError_WhenPlanIdMissing()
    {
        var state = SetupDefaults("exec-gos-test-4");
        state.PendingPlans.Clear();
        var tool = CreateTool();

        var result = await tool.ExecuteCreateGlobalOptionSetAsync(
            "missing", "cr123_priority", "TestSolution", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _mockDataverseClient.Verify(
            c => c.CreateGlobalOptionSetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
