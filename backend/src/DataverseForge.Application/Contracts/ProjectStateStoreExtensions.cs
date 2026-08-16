using System.Collections.Concurrent;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Contracts;

/// <summary>
/// Extension helpers on <see cref="IProjectStateStore"/> for the
/// "stale-state write race" pattern documented in the audit (F-23).
///
/// Background: every Execute_* / Delete_* tool reads <see cref="ProjectState"/>
/// at the top of the method, awaits 5–30 seconds of Dataverse work, then
/// saves the mutated state. If a concurrent flow (parallel agent action,
/// SSE handoff message, audit emitter) wrote to the SAME session during
/// that window, the tool's `Save(state)` silently overwrites it — the
/// concurrent write is lost. Sessions are usually single-threaded but
/// not always (parallel get_app_full + handoff_to_tester is a real
/// pattern in the codebase).
///
/// The fix shipped on <c>ScaffoldAppTool</c> (commit 0faca9a) and
/// <c>TeardownAppTool</c> (a291d45) was to re-read state immediately
/// before mutating + saving — so any concurrent writes that landed during
/// the Dataverse work survive.
///
/// This helper formalizes that pattern so the remaining ~20 tools can
/// adopt it via a single method call instead of duplicating the
/// read-mutate-save sequence by hand.
/// </summary>
public static class ProjectStateStoreExtensions
{
    // Per-session write lock. The in-memory store returns a shared object
    // reference (so mutations at least land on the same instance), but the
    // SQLite store deserializes a FRESH copy on every GetOrCreate — so two
    // concurrent ApplyAndSaveAsync calls would each read, mutate their own
    // copy, and save → the earlier write is lost (the exact F-23 race). We
    // serialize the read-mutate-save per session here so it's correct for ANY
    // store impl. Bounded in practice by the number of live sessions.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    /// <summary>
    /// Re-reads the latest <see cref="ProjectState"/>, applies <paramref name="mutate"/>
    /// to the fresh copy, and saves it back. Use after any Dataverse write
    /// that took non-trivial time, instead of saving an old in-memory copy.
    ///
    /// <paramref name="mutate"/> runs synchronously against the freshly-read
    /// state. Capture all the values you want to persist (new artifact id,
    /// audit detail, planId, etc.) as locals BEFORE calling this method, then
    /// reference those locals from inside the lambda — that way the closure
    /// only depends on stable data, not on any state object.
    /// </summary>
    public static async Task ApplyAndSaveAsync(
        this IProjectStateStore store,
        string sessionId,
        Action<ProjectState> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mutate);

        var gate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var fresh = await store.GetOrCreateAsync(sessionId, cancellationToken);
            mutate(fresh);
            await store.SaveAsync(sessionId, fresh, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
