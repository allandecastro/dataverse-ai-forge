using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateSecurityRoleToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateSecurityRoleTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateSecurityRoleTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mockDataverseClient
            .Setup(c => c.CreateSecurityRoleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        return state;
    }

    [Fact]
    public async Task ExecuteCreateSecurityRole_CallsCreateSecurityRoleAsync()
    {
        var sessionId = "exec-secrole-test-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateSecurityRoleAsync(
            "{\"name\":\"Work Order Contributor\"}", "Work Order Contributor", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateSecurityRoleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateSecurityRole_AddsToStateSecurityRoles()
    {
        var sessionId = "exec-secrole-test-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateSecurityRoleAsync(
            "{\"name\":\"Work Order Contributor\"}", "Work Order Contributor", CancellationToken.None);

        Assert.Single(state.SecurityRoles);
        Assert.Equal("Work Order Contributor", state.SecurityRoles[0].Name);
    }

    [Fact]
    public async Task ExecuteCreateSecurityRole_PushesToOperationStack()
    {
        var sessionId = "exec-secrole-test-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateSecurityRoleAsync(
            "{\"name\":\"Work Order Contributor\"}", "Work Order Contributor", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal(AgentNames.SecurityBuilder, op.AgentName);
        Assert.Equal("ExecuteCreateSecurityRole", op.ToolName);
        Assert.True(op.IsReversible);
        Assert.NotNull(op.ReverseOp);
    }

    [Fact]
    public async Task ExecuteCreateSecurityRole_SavesState()
    {
        var sessionId = "exec-secrole-test-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateSecurityRoleAsync(
            "{\"name\":\"Work Order Contributor\"}", "Work Order Contributor", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
