using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tests.Fakes;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PublishAllToolTests
{
    private const string SessionId = "publish-all-session";
    private const string ConcurrentTool = "ConcurrentWrite";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly CloningProjectStateStore _store = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    public PublishAllToolTests() =>
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(SessionId);

    private PublishAllTool CreateTool() =>
        new(_mockDataverseClient.Object, _store, _mockSessionContext.Object, NullLogger<PublishAllTool>.Instance);

    [Fact]
    public async Task PublishAll_SuccessPath_PreservesConcurrentWriteLandedDuringPublish()
    {
        // A concurrent write lands DURING the (multi-minute) publish call.
        _mockDataverseClient
            .Setup(c => c.PublishAllAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => _store.SimulateConcurrentWrite(SessionId, s =>
                s.AuditLog.Add(AuditEntry.Ok(AgentNames.Tester, ConcurrentTool, "landed mid-publish"))));

        await CreateTool().PublishAllAsync(CancellationToken.None);

        var saved = await _store.GetOrCreateAsync(SessionId);
        Assert.Contains(saved.AuditLog, e => e.Action == ConcurrentTool);   // not clobbered
        Assert.Contains(saved.AuditLog, e => e.Action == "PublishAll");      // own entry persisted
    }

    [Fact]
    public async Task PublishAll_FailurePath_PreservesConcurrentWrite_AndRecordsFailure()
    {
        _mockDataverseClient
            .Setup(c => c.PublishAllAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                _store.SimulateConcurrentWrite(SessionId, s =>
                    s.AuditLog.Add(AuditEntry.Ok(AgentNames.Tester, ConcurrentTool, "landed mid-publish")));
                throw new InvalidOperationException("publish boom");
            });

        var result = await CreateTool().PublishAllAsync(CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        var saved = await _store.GetOrCreateAsync(SessionId);
        Assert.Contains(saved.AuditLog, e => e.Action == ConcurrentTool);
        Assert.Contains(saved.AuditLog, e => e.Action == "PublishAll" && !e.Success);
    }
}
