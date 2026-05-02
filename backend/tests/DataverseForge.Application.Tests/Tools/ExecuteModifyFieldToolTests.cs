using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteModifyFieldToolTests
{
    private const string PlanId = "modify_field_test";
    private const string PayloadJson = "{\"DisplayName\":\"...\"}";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteModifyFieldTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteModifyFieldTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.PendingPlans[PlanId] = PayloadJson;
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.ModifyFieldAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return state;
    }

    [Fact]
    public async Task ExecuteModifyField_CallsModifyField_WithLogicalNamesAndStoredPayload()
    {
        SetupDefaults("exec-modfld-test-1");
        var tool = CreateTool();

        await tool.ExecuteModifyFieldAsync(
            PlanId, "cr123_project", "cr123_name", "displayName", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.ModifyFieldAsync("cr123_project", "cr123_name", PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteModifyField_PushesToOperationStack_AndRemovesPlan()
    {
        var state = SetupDefaults("exec-modfld-test-2");
        var tool = CreateTool();

        await tool.ExecuteModifyFieldAsync(
            PlanId, "cr123_project", "cr123_name", "displayName", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteModifyField", op.ToolName);
        Assert.False(op.IsReversible);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
    }

    [Fact]
    public async Task ExecuteModifyField_ReturnsConfirmationMessage()
    {
        SetupDefaults("exec-modfld-test-3");
        var tool = CreateTool();

        var result = await tool.ExecuteModifyFieldAsync(
            PlanId, "cr123_project", "cr123_name", "displayName, requiredLevel", CancellationToken.None);

        Assert.Contains("cr123_name", result);
        Assert.Contains("displayName, requiredLevel", result);
    }

    [Fact]
    public async Task ExecuteModifyField_ReturnsError_WhenPlanIdMissing()
    {
        var state = SetupDefaults("exec-modfld-test-4");
        state.PendingPlans.Clear();
        var tool = CreateTool();

        var result = await tool.ExecuteModifyFieldAsync(
            "missing", "cr123_project", "cr123_name", "x", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _mockDataverseClient.Verify(
            c => c.ModifyFieldAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
