using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateBpfToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateBpfTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mockDataverseClient
            .Setup(c => c.CreateBpfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        return state;
    }

    [Fact]
    public async Task ExecuteCreateBpf_CallsCreateBpfAsync()
    {
        var sessionId = "exec-bpf-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateBpfAsync("{}", "cr123_WorkOrderProcess", "cr123_workorder", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateBpfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateBpf_AddsToStateBusinessProcessFlows()
    {
        var sessionId = "exec-bpf-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateBpfAsync("{}", "cr123_WorkOrderProcess", "cr123_workorder", CancellationToken.None);

        Assert.Single(state.BusinessProcessFlows);
        Assert.Equal("cr123_WorkOrderProcess", state.BusinessProcessFlows[0].Name);
        Assert.Equal("cr123_workorder", state.BusinessProcessFlows[0].PrimaryTable);
    }

    [Fact]
    public async Task ExecuteCreateBpf_PushesToOperationStack()
    {
        var sessionId = "exec-bpf-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateBpfAsync("{}", "cr123_WorkOrderProcess", "cr123_workorder", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateBpf", op.ToolName);
        Assert.Equal(AgentNames.AutomationBuilder, op.AgentName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateBpf_SavesState()
    {
        var sessionId = "exec-bpf-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateBpfAsync("{}", "cr123_WorkOrderProcess", "cr123_workorder", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
