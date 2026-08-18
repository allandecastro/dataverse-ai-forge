using System.Collections.Concurrent;

namespace DataverseForge.Infrastructure.Concurrency;

/// <summary>
/// Per-session write serialization (the complete F-23 fix). Every ProjectState
/// mutation happens inside a request — a turn on <c>/api/agui</c> or a session
/// CRUD call; there are NO background writers — so holding one lock per session
/// for the duration of the write path guarantees two concurrent turns on the
/// same session (e.g. two browser tabs) can never interleave their
/// read-modify-write cycles and lose an update, regardless of whether a tool
/// uses <c>ApplyAndSaveAsync</c> or a raw GetOrCreate+Save. Different sessions
/// use different locks, so they never block each other.
/// </summary>
public sealed class SessionLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>
    /// Acquires the session's lock, or returns <c>null</c> if it isn't free
    /// within <paramref name="timeout"/> (another turn is already running for
    /// this session — the caller should fail fast, e.g. HTTP 409). Dispose the
    /// returned handle to release.
    /// </summary>
    public async Task<IDisposable?> TryAcquireAsync(
        string sessionId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(timeout, cancellationToken))
            return null;
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                gate.Release();
        }
    }
}
