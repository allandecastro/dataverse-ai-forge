using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateFieldSecurityProfileToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateFieldSecurityProfileTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateFieldSecurityProfileTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mockDataverseClient
            .Setup(c => c.CreateFieldSecurityProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        return state;
    }

    [Fact]
    public async Task ExecuteCreateFieldSecurityProfile_CallsCreateFieldSecurityProfileAsync()
    {
        var sessionId = "exec-fsp-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateFieldSecurityProfileAsync("{}", "PII Access - HR Team", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateFieldSecurityProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateFieldSecurityProfile_AddsToStateSecurityRoles()
    {
        var sessionId = "exec-fsp-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateFieldSecurityProfileAsync("{}", "PII Access - HR Team", CancellationToken.None);

        Assert.Single(state.SecurityRoles);
        Assert.StartsWith("[FieldSecurity]", state.SecurityRoles[0].Name);
        Assert.Contains("PII Access - HR Team", state.SecurityRoles[0].Name);
    }

    [Fact]
    public async Task ExecuteCreateFieldSecurityProfile_PushesToOperationStack()
    {
        var sessionId = "exec-fsp-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateFieldSecurityProfileAsync("{}", "PII Access - HR Team", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateFieldSecurityProfile", op.ToolName);
        Assert.Equal(AgentNames.SecurityBuilder, op.AgentName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateFieldSecurityProfile_SavesState()
    {
        var sessionId = "exec-fsp-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateFieldSecurityProfileAsync("{}", "PII Access - HR Team", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
