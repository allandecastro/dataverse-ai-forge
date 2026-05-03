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

    private (ProjectState state, string planId) SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        var planId = $"create_alt_key_{Guid.NewGuid():N}";
        state.AddPendingPlan(planId, "{\"LogicalName\":\"cr123_externalidkey\",\"KeyAttributes\":[\"cr123_externalid\"]}");

        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateAlternateKeyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return (state, planId);
    }

    [Fact]
    public async Task ExecuteAlternateKey_CallsCreateAlternateKey_WithLogicalNameAndStoredPayload()
    {
        var sessionId = "exec-altkey-test-1";
        var (_, planId) = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateAlternateKeyAsync(
            planId, "cr123_project", "cr123_externalidkey", "External ID", CancellationToken.None);

        // The payload comes from server-side state, NOT from the agent — verify
        // the tool passed the stored JSON to the client.
        _mockDataverseClient.Verify(
            c => c.CreateAlternateKeyAsync(
                "cr123_project",
                It.Is<string>(p => p.Contains("cr123_externalidkey")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAlternateKey_AddsToStateAlternateKeys()
    {
        var sessionId = "exec-altkey-test-2";
        var (state, planId) = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateAlternateKeyAsync(
            planId, "cr123_project", "cr123_externalidkey", "External ID", CancellationToken.None);

        Assert.Single(state.AlternateKeys);
        Assert.Equal("cr123_externalidkey", state.AlternateKeys[0].KeyLogicalName);
        Assert.Equal("cr123_project", state.AlternateKeys[0].TableLogicalName);
    }

    [Fact]
    public async Task ExecuteAlternateKey_PushesToOperationStack()
    {
        var sessionId = "exec-altkey-test-3";
        var (state, planId) = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateAlternateKeyAsync(
            planId, "cr123_project", "cr123_externalidkey", "External ID", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateAlternateKey", op.ToolName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteAlternateKey_RejectedPlanId_IsSkipped()
    {
        var sessionId = "exec-altkey-test-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.ExecuteCreateAlternateKeyAsync(
            "rejected_alt_key_abc123", "cr123_project", "cr123_externalidkey", "External ID", CancellationToken.None);

        Assert.Contains("SKIPPED", result);
        _mockDataverseClient.Verify(
            c => c.CreateAlternateKeyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAlternateKey_UnknownPlanId_ReturnsError()
    {
        var sessionId = "exec-altkey-test-5";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.ExecuteCreateAlternateKeyAsync(
            "create_alt_key_doesnotexist", "cr123_project", "cr123_externalidkey", "External ID", CancellationToken.None);

        Assert.Contains("ERROR", result);
        _mockDataverseClient.Verify(
            c => c.CreateAlternateKeyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
