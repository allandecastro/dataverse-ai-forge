using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Contracts;

public class ProjectStateStoreExtensionsTests
{
    [Fact]
    public async Task ApplyAndSaveAsync_RereadsBeforeMutating_AndSavesFreshCopy()
    {
        // The race we're protecting against: tool reads state at time T1,
        // a concurrent flow writes at T2, tool tries to save at T3 with the
        // T1 snapshot — T2's writes vanish. ApplyAndSaveAsync re-reads at
        // T3 so T2's writes survive.
        var sessionId = "race-session";
        var staleSnapshot = new ProjectState { ProjectId = sessionId };
        staleSnapshot.AuditLog.Add(new AuditEntry { Action = "stale-only", EntryId = "stale" });

        var freshSnapshot = new ProjectState { ProjectId = sessionId };
        freshSnapshot.AuditLog.Add(new AuditEntry { Action = "stale-only", EntryId = "stale" });
        freshSnapshot.AuditLog.Add(new AuditEntry { Action = "concurrent-write", EntryId = "concurrent" });

        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(freshSnapshot);
        ProjectState? saved = null;
        store.Setup(s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string _, ProjectState p, CancellationToken _) => saved = p)
            .Returns(Task.CompletedTask);

        await store.Object.ApplyAndSaveAsync(sessionId, fresh =>
        {
            fresh.AuditLog.Add(new AuditEntry { Action = "tool-write", EntryId = "tool" });
        });

        Assert.NotNull(saved);
        // The concurrent write survives — that's the whole point.
        Assert.Contains(saved!.AuditLog, e => e.EntryId == "concurrent");
        // And the tool's mutation landed on the fresh snapshot.
        Assert.Contains(saved.AuditLog, e => e.EntryId == "tool");
        // The stale-only entry is also there because it was on both snapshots.
        Assert.Contains(saved.AuditLog, e => e.EntryId == "stale");
    }

    [Fact]
    public async Task ApplyAndSaveAsync_NullStore_Throws()
    {
        IProjectStateStore? store = null;
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store!.ApplyAndSaveAsync("s", _ => { }));
    }

    [Fact]
    public async Task ApplyAndSaveAsync_NullMutate_Throws()
    {
        var store = new Mock<IProjectStateStore>();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.Object.ApplyAndSaveAsync("s", null!));
    }

    [Fact]
    public async Task ApplyAndSaveAsync_PassesCancellationTokenToBothCalls()
    {
        var store = new Mock<IProjectStateStore>();
        var passedToGet = default(CancellationToken);
        var passedToSave = default(CancellationToken);
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, CancellationToken ct) => passedToGet = ct)
            .ReturnsAsync(new ProjectState());
        store.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string _, ProjectState _, CancellationToken ct) => passedToSave = ct)
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        await store.Object.ApplyAndSaveAsync("s", _ => { }, cts.Token);

        Assert.Equal(cts.Token, passedToGet);
        Assert.Equal(cts.Token, passedToSave);
    }
}
