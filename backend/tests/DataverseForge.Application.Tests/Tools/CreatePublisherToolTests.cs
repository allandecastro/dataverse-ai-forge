using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

/// <summary>
/// The first tool in every NEW-solution onboarding flow. Untested historically;
/// covers the input validation (prefix length / charset, optionValuePrefix
/// range), the idempotency precheck (already-exists returns NOOP not a
/// confusing 412), and the operation-stack + audit-log writes.
/// </summary>
public class CreatePublisherToolTests
{
    private readonly Mock<IDataverseClient> _client = new();
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ISessionContext> _ctx = new();

    private CreatePublisherTool CreateTool() => new(
        _client.Object,
        _store.Object,
        _ctx.Object,
        NullLogger<CreatePublisherTool>.Instance);

    private ProjectState SetupSession(string sessionId, IReadOnlyList<PublisherInfo>? existing = null)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _ctx.Setup(c => c.SessionId).Returns(sessionId);
        _store.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _client.Setup(c => c.GetPublishersAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(existing ?? Array.Empty<PublisherInfo>());
        return state;
    }

    [Fact]
    public async Task HappyPath_CallsClient_PushesOpAndAudit()
    {
        var state = SetupSession("pub-1");
        var newId = Guid.NewGuid();
        _client.Setup(c => c.CreatePublisherAsync(
                "ContosoCorp", "Contoso Corp", "Test", "contoso", 12345, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newId);
        var tool = CreateTool();

        var result = await tool.CreatePublisherAsync(
            "ContosoCorp", "Contoso Corp", "Test", "contoso", 12345, CancellationToken.None);

        Assert.Contains(newId.ToString(), result);
        Assert.Single(state.OperationStack);
        Assert.Single(state.AuditLog);
        Assert.Equal("CreatePublisher", state.OperationStack.Peek().ToolName);
        _store.Verify(s => s.SaveAsync("pub-1", state, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("a")]               // too short
    [InlineData("toolongprefix")]   // too long (>8)
    [InlineData("bad_pre")]         // underscore
    [InlineData("bad-pre")]         // hyphen
    public async Task InvalidPrefix_ReturnsError_WithoutCallingClient(string prefix)
    {
        SetupSession("pub-2");
        var tool = CreateTool();

        var result = await tool.CreatePublisherAsync(
            "X", "X", "X", prefix, 12345, CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("customizationPrefix", result);
        _client.Verify(c => c.CreatePublisherAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(9999)]   // below range
    [InlineData(100000)] // above range
    public async Task InvalidOptionValuePrefix_ReturnsError(int prefix)
    {
        SetupSession("pub-3");
        var tool = CreateTool();

        var result = await tool.CreatePublisherAsync(
            "X", "X", "X", "valid", prefix, CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("customizationOptionValuePrefix", result);
    }

    [Fact]
    public async Task ExistingPublisher_ReturnsNoop_WithoutCreating()
    {
        // Precheck must find the existing publisher and short-circuit. Prevents
        // the historical confusion where Dataverse 412'd and the agent claimed
        // success anyway.
        var existingId = Guid.NewGuid();
        SetupSession("pub-4", new[]
        {
            new PublisherInfo(existingId, "ContosoCorp", "Contoso Corp", "contoso", 12345),
        });
        var tool = CreateTool();

        var result = await tool.CreatePublisherAsync(
            "ContosoCorp", "Contoso Corp", "Test", "contoso", 12345, CancellationToken.None);

        Assert.StartsWith("NOOP:", result);
        Assert.Contains(existingId.ToString(), result);
        _client.Verify(c => c.CreatePublisherAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
