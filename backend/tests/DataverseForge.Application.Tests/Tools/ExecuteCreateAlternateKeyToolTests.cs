using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateAlternateKeyToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateAlternateKeyTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateAlternateKeyTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateAlternateKeyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return state;
    }

    [Fact]
    public async Task ExecuteAlternateKey_CallsCreateAlternateKey()
    {
        var sessionId = "exec-altkey-test-1";
        SetupDefaults(sessionId);
        var tableMetadataId = Guid.NewGuid();
        var tool = CreateTool();

        await tool.ExecuteCreateAlternateKeyAsync(
            tableMetadataId.ToString(), "cr123_project", "{}", "cr123_externalidkey", "External ID", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateAlternateKeyAsync(tableMetadataId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAlternateKey_AddsToStateAlternateKeys()
    {
        var sessionId = "exec-altkey-test-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateAlternateKeyAsync(
            Guid.NewGuid().ToString(), "cr123_project", "{}", "cr123_externalidkey", "External ID", CancellationToken.None);

        Assert.Single(state.AlternateKeys);
        Assert.Equal("cr123_externalidkey", state.AlternateKeys[0].KeyLogicalName);
        Assert.Equal("cr123_project", state.AlternateKeys[0].TableLogicalName);
    }

    [Fact]
    public async Task ExecuteAlternateKey_PushesToOperationStack()
    {
        var sessionId = "exec-altkey-test-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateAlternateKeyAsync(
            Guid.NewGuid().ToString(), "cr123_project", "{}", "cr123_externalidkey", "External ID", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateAlternateKey", op.ToolName);
        Assert.True(op.IsReversible);
    }
}
