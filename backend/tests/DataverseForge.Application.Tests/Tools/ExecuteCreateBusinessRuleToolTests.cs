using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateBusinessRuleToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateBusinessRuleTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mockDataverseClient
            .Setup(c => c.CreateBusinessRuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        return state;
    }

    [Fact]
    public async Task ExecuteCreateBusinessRule_CallsCreateBusinessRuleAsync()
    {
        var sessionId = "exec-br-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateBusinessRuleAsync("{}", "Set Priority Required", "cr123_workorder", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateBusinessRuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateBusinessRule_AddsToStateBusinessRules()
    {
        var sessionId = "exec-br-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateBusinessRuleAsync("{}", "Set Priority Required", "cr123_workorder", CancellationToken.None);

        Assert.Single(state.BusinessRules);
        Assert.Equal("Set Priority Required", state.BusinessRules[0].Name);
        Assert.Equal("cr123_workorder", state.BusinessRules[0].TableLogicalName);
    }

    [Fact]
    public async Task ExecuteCreateBusinessRule_PushesToOperationStack()
    {
        var sessionId = "exec-br-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateBusinessRuleAsync("{}", "Set Priority Required", "cr123_workorder", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateBusinessRule", op.ToolName);
        Assert.Equal(AgentNames.AutomationBuilder, op.AgentName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateBusinessRule_SavesState()
    {
        var sessionId = "exec-br-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateBusinessRuleAsync("{}", "Set Priority Required", "cr123_workorder", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
