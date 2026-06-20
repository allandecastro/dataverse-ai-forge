using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tests.Fakes;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PublishAppToolTests
{
    private const string SessionId = "publish-app-session";
    private const string ConcurrentTool = "ConcurrentWrite";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly CloningProjectStateStore _store = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    public PublishAppToolTests() =>
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(SessionId);

    private PublishAppTool CreateTool() =>
        new(_mockDataverseClient.Object, _store, _mockSessionContext.Object, NullLogger<PublishAppTool>.Instance);

    [Fact]
    public async Task PublishApp_PreservesConcurrentWriteLandedDuringPublish()
    {
        var appId = Guid.NewGuid();
        _mockDataverseClient
            .Setup(c => c.PublishAppAsync(appId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => _store.SimulateConcurrentWrite(SessionId, s =>
                s.AuditLog.Add(AuditEntry.Ok(AgentNames.Tester, ConcurrentTool, "landed mid-publish"))));

        await CreateTool().PublishAppAsync(appId.ToString(), CancellationToken.None);

        var saved = await _store.GetOrCreateAsync(SessionId);
        Assert.Contains(saved.AuditLog, e => e.Action == ConcurrentTool);   // not clobbered
        Assert.Contains(saved.AuditLog, e => e.Action == "PublishApp");      // own entry persisted
    }

    [Fact]
    public async Task PublishApp_ReturnsError_WhenAppIdNotAGuid()
    {
        var result = await CreateTool().PublishAppAsync("not-a-guid", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _mockDataverseClient.Verify(
            c => c.PublishAppAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
