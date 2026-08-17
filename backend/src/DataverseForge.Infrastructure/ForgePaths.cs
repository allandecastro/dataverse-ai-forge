namespace DataverseForge.Infrastructure;

/// <summary>
/// Single source of truth for runtime data paths (state.db, rag/, exports/,
/// user-settings.json, .secret.key).
///
/// Resolution order (first non-empty wins):
///   1. The <c>DVFORGE_DATA_DIR</c> environment variable. This is an OPTIONAL
///      operator/Docker override (e.g. a mounted host volume) — end users of the
///      desktop app never set it. When set it pins the location and disables the
///      in-app "Change location" flow.
///   2. The pointer file the user chose in-app (persisted in the OS config dir,
///      so it survives restarts and rebuilds). This is the normal desktop path.
///   3. Default: the per-user OS app-data dir (<c>{ApplicationData}/dataverseforge/data</c>,
///      e.g. ~/Library/Application Support on macOS, %APPDATA% on Windows). A
///      real default that persists across app updates and is writable — unlike
///      the old next-to-binary location.
///
/// Changing the location is deferred-and-safe: <see cref="RequestMove"/> writes
/// the new pointer plus a "pending move" marker and the app restarts;
/// <see cref="ExecutePendingMove"/> runs on the next resolve, BEFORE any SQLite
/// connection opens, so no open file is ever moved.
/// </summary>
public static class ForgePaths
{
    public const string DataDirEnvVar = "DVFORGE_DATA_DIR";

    private static readonly Lazy<string> _dataDir = new(ResolveDataDir);

    public static string GetWritableDataDir() => _dataDir.Value;

    public static string GetSubdir(string subdir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subdir);
        var path = Path.Combine(GetWritableDataDir(), subdir);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>True when the location is pinned by the environment variable, in
    /// which case an in-app change would be overridden.</summary>
    public static bool IsPinnedByEnv =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataDirEnvVar));

    // ─── OS config dir (holds the pointer + pending-move marker) ───
    private static string ConfigDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "dataverseforge");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string PointerPath() => Path.Combine(ConfigDir(), "datadir");
    private static string MoveMarkerPath() => Path.Combine(ConfigDir(), "pending-move");

    /// <summary>
    /// Records a location change for the NEXT startup: validates the target,
    /// writes the pointer + a pending-move marker (current → target). Does not
    /// move anything now (the current files are open). Returns the resolved
    /// absolute target. Throws <see cref="ArgumentException"/> on a bad target.
    /// </summary>
    public static string RequestMove(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (!Path.IsPathFullyQualified(target))
            throw new ArgumentException("Target must be an absolute path.", nameof(target));

        var current = GetWritableDataDir();
        var resolvedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(current)), resolvedTarget,
                StringComparison.Ordinal))
            throw new ArgumentException("Target is the same as the current location.", nameof(target));

        Directory.CreateDirectory(resolvedTarget); // also proves it's writable

        File.WriteAllText(MoveMarkerPath(), $"{current}\n{resolvedTarget}");
        File.WriteAllText(PointerPath(), resolvedTarget);
        return resolvedTarget;
    }

    private static string ResolveDataDir()
    {
        ExecutePendingMove();

        var fromEnv = Environment.GetEnvironmentVariable(DataDirEnvVar);
        string dir;
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            dir = fromEnv;
        }
        else
        {
            var pointer = ReadPointer();
            dir = !string.IsNullOrWhiteSpace(pointer) ? pointer : DefaultDataDir();
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// The default data dir when nothing is pinned or chosen: the per-user OS
    /// app-data dir (same base as the pointer/config so everything lives under
    /// one <c>dataverseforge</c> folder per user). Falls back to next-to-binary
    /// only if the OS reports no app-data dir (rare; e.g. a container without
    /// HOME — those set <see cref="DataDirEnvVar"/> anyway).
    /// </summary>
    private static string DefaultDataDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(appData, "dataverseforge", "data");
    }

    private static string? ReadPointer()
    {
        try
        {
            var p = PointerPath();
            if (File.Exists(p))
            {
                var v = File.ReadAllText(p).Trim();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        catch { /* unreadable pointer — fall through to default */ }
        return null;
    }

    /// <summary>
    /// Runs any pending move recorded by <see cref="RequestMove"/>, moving the
    /// whole data directory's contents source → target. Runs before any DB
    /// connection is opened. Best-effort per entry; only clears the marker when
    /// the move completes, so a failure retries on the next start.
    /// </summary>
    private static void ExecutePendingMove()
    {
        // When the location is pinned by the env var, in-app choices (pointer AND
        // pending move) are ignored entirely — an operator override wins. This
        // also stops a second process from applying the app's queued move: e.g.
        // a test host or CLI booting with DVFORGE_DATA_DIR set must NOT relocate
        // the user's real data dir behind their back.
        if (IsPinnedByEnv) return;

        string marker;
        try
        {
            marker = MoveMarkerPath();
            if (!File.Exists(marker)) return;
        }
        catch { return; }

        try
        {
            var lines = File.ReadAllLines(marker);
            if (lines.Length < 2) { File.Delete(marker); return; }
            var source = lines[0].Trim();
            var target = lines[1].Trim();

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)
                || string.Equals(source, target, StringComparison.Ordinal)
                || !Directory.Exists(source))
            {
                File.Delete(marker);
                return;
            }

            MoveDirContents(source, target);
            File.Delete(marker);
        }
        catch
        {
            // Leave the marker so the move retries next start rather than
            // stranding data half-moved.
        }
    }

    private static void MoveDirContents(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            var dst = Path.Combine(target, Path.GetFileName(file));
            File.Move(file, dst, overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            MoveDirContents(dir, Path.Combine(target, Path.GetFileName(dir)));
            try { Directory.Delete(dir); } catch { /* non-empty/locked — leave it */ }
        }
    }
}
