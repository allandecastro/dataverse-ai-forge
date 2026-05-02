using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteAddOptionToOptionSetToolTests
{
    private const string PlanId = "add_option_test";
    private const string PayloadJson = "{\"OptionSetName\":\"cr123_priority\"}";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteAddOptionToOptionSetTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteAddOptionToOptionSetTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.PendingPlans[PlanId] = PayloadJson;
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.InsertOptionValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return state;
    }

    [Fact]
    public async Task ExecuteAddOption_CallsInsertOptionValue_WithStoredPayload()
    {
        SetupDefaults("exec-addopt-test-1");
        var tool = CreateTool();

        await tool.ExecuteAddOptionToOptionSetAsync(PlanId, "cr123_priority", true, 100003, "Critical", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.InsertOptionValueAsync(PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAddOption_PushesToOperationStack_AndRemovesPlan()
    {
        var state = SetupDefaults("exec-addopt-test-2");
        var tool = CreateTool();

        await tool.ExecuteAddOptionToOptionSetAsync(PlanId, "cr123_priority", true, 100003, "Critical", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteAddOptionToOptionSet", op.ToolName);
        Assert.True(op.IsReversible);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
    }

    [Fact]
    public async Task ExecuteAddOption_ReturnsConfirmationMessage()
    {
        SetupDefaults("exec-addopt-test-3");
        var tool = CreateTool();

        var result = await tool.ExecuteAddOptionToOptionSetAsync(PlanId, "cr123_priority", true, 100003, "Critical", CancellationToken.None);

        Assert.Contains("Critical", result);
        Assert.Contains("100003", result);
    }

    [Fact]
    public async Task ExecuteAddOption_ReturnsError_WhenPlanIdMissing()
    {
        var state = SetupDefaults("exec-addopt-test-4");
        state.PendingPlans.Clear();
        var tool = CreateTool();

        var result = await tool.ExecuteAddOptionToOptionSetAsync("missing", "cr123_priority", true, 100003, "Critical", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _mockDataverseClient.Verify(
            c => c.InsertOptionValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
