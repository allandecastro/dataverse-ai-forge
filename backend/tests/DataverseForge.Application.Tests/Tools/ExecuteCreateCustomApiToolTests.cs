using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateCustomApiToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateCustomApiTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateCustomApiTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mockDataverseClient
            .Setup(c => c.CreateCustomApiAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        return state;
    }

    [Fact]
    public async Task ExecuteCreateCustomApi_CallsCreateCustomApiAsync()
    {
        var sessionId = "exec-capi-test-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var payloadJson = "{\"uniquename\":\"cr123_TestApi\"}";
        await tool.ExecuteCreateCustomApiAsync(payloadJson, "cr123_TestApi", "Test API", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateCustomApiAsync(payloadJson, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateCustomApi_AddsToStatePlugins()
    {
        var sessionId = "exec-capi-test-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateCustomApiAsync("{}", "cr123_TestApi", "Test API", CancellationToken.None);

        Assert.Single(state.Plugins);
        Assert.Equal("Test API", state.Plugins[0].Name);
        Assert.Equal("CustomApi", state.Plugins[0].Message);
    }

    [Fact]
    public async Task ExecuteCreateCustomApi_PushesToOperationStack()
    {
        var sessionId = "exec-capi-test-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateCustomApiAsync("{}", "cr123_TestApi", "Test API", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal(AgentNames.AutomationBuilder, op.AgentName);
        Assert.Equal("ExecuteCreateCustomApi", op.ToolName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateCustomApi_SavesState()
    {
        var sessionId = "exec-capi-test-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateCustomApiAsync("{}", "cr123_TestApi", "Test API", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
