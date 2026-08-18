using DataverseForge.Infrastructure.Concurrency;

namespace DataverseForge.Infrastructure.Tests.Concurrency;

public class SessionLocksTests
{
    [Fact]
    public async Task SecondAcquire_SameSession_TimesOutToNull()
    {
        var locks = new SessionLocks();
        using var first = await locks.TryAcquireAsync("s1", TimeSpan.FromSeconds(1));
        Assert.NotNull(first);

        var second = await locks.TryAcquireAsync("s1", TimeSpan.FromMilliseconds(50));
        Assert.Null(second); // held by `first`
    }

    [Fact]
    public async Task Release_LetsNextAcquireSucceed()
    {
        var locks = new SessionLocks();
        var first = await locks.TryAcquireAsync("s1", TimeSpan.FromSeconds(1));
        first!.Dispose();

        using var second = await locks.TryAcquireAsync("s1", TimeSpan.FromMilliseconds(50));
        Assert.NotNull(second);
    }

    [Fact]
    public async Task DifferentSessions_DoNotBlockEachOther()
    {
        var locks = new SessionLocks();
        using var a = await locks.TryAcquireAsync("a", TimeSpan.FromSeconds(1));
        using var b = await locks.TryAcquireAsync("b", TimeSpan.FromMilliseconds(50));
        Assert.NotNull(a);
        Assert.NotNull(b);
    }
}
