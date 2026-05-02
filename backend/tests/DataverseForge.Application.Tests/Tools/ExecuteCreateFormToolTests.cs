using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateFormToolTests
{
    private const string PlanId = "create_form_test";
    private const string PayloadJson = "{\"name\":\"WO Main Form\"}";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateFormTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateFormTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.PendingPlans[PlanId] = PayloadJson;
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateFormAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return state;
    }

    [Fact]
    public async Task ExecuteCreateForm_LooksUpPayloadByPlanId_AndCallsCreate()
    {
        SetupDefaults("exec-form-1");
        var tool = CreateTool();

        await tool.ExecuteCreateFormAsync(PlanId, "cr123_workorder", "WO Main Form", "main", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateFormAsync(PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateForm_AddsToStateForms_AndRemovesPlan()
    {
        var state = SetupDefaults("exec-form-2");
        var tool = CreateTool();

        await tool.ExecuteCreateFormAsync(PlanId, "cr123_workorder", "WO Main Form", "main", CancellationToken.None);

        Assert.Single(state.Forms);
        Assert.Equal("cr123_workorder", state.Forms[0].TableLogicalName);
        Assert.Equal("WO Main Form", state.Forms[0].Name);
        Assert.Equal("main", state.Forms[0].FormType);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
    }

    [Fact]
    public async Task ExecuteCreateForm_PushesToOperationStack()
    {
        var state = SetupDefaults("exec-form-3");
        var tool = CreateTool();

        await tool.ExecuteCreateFormAsync(PlanId, "cr123_workorder", "WO Main Form", "main", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateForm", op.ToolName);
        Assert.Equal(AgentNames.UIBuilder, op.AgentName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateForm_ReturnsError_WhenPlanIdMissing()
    {
        var state = SetupDefaults("exec-form-4");
        state.PendingPlans.Clear();
        var tool = CreateTool();

        var result = await tool.ExecuteCreateFormAsync("missing", "cr123_workorder", "WO Main Form", "main", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _mockDataverseClient.Verify(
            c => c.CreateFormAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
