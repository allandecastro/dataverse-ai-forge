namespace DataverseForge.Infrastructure;

/// <summary>
/// Single source of truth for runtime data paths (exports/, user-settings.json, etc.).
///
/// Background — audit findings F-43, F-44 flagged that runtime artifacts
/// were being written under <c>AppContext.BaseDirectory</c> (which is
/// <c>/app</c> in the Docker image — NOT a mounted volume). That meant
/// solution exports vanished on container restart and saved Dataverse
/// credentials were wiped on rebuild. Read-only filesystems also crashed
/// with cryptic UnauthorizedAccessException.
///
/// Resolution rule:
///   1. If the <c>DVFORGE_DATA_DIR</c> environment variable is set and
///      non-empty → use it verbatim. Operators mount this to a host
///      volume in production.
///   2. Otherwise fall back to <c>{AppContext.BaseDirectory}/data</c> —
///      works for <c>dotnet run</c> in dev (writes alongside the bin
///      output) and is at least co-located with the binary.
///
/// All callers go through <see cref="GetWritableDataDir"/>; nothing
/// constructs a path under AppContext.BaseDirectory directly anymore.
/// </summary>
public static class ForgePaths
{
    public const string DataDirEnvVar = "DVFORGE_DATA_DIR";

    private static readonly Lazy<string> _dataDir = new(ResolveDataDir);

    /// <summary>
    /// Returns a writable directory for runtime artifacts. Creates the
    /// directory on first access; subsequent calls return the cached
    /// path without re-creating. Throws on the first call if the
    /// configured path is unwritable (better to fail loudly at startup
    /// than to silently lose artifacts).
    /// </summary>
    public static string GetWritableDataDir() => _dataDir.Value;

    /// <summary>
    /// <see cref="GetWritableDataDir"/> joined with <paramref name="subdir"/>.
    /// Creates the subdirectory if missing.
    /// </summary>
    public static string GetSubdir(string subdir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subdir);
        var path = Path.Combine(GetWritableDataDir(), subdir);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolveDataDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable(DataDirEnvVar);
        var dir = !string.IsNullOrWhiteSpace(fromEnv)
            ? fromEnv
            : Path.Combine(AppContext.BaseDirectory, "data");

        Directory.CreateDirectory(dir);
        return dir;
    }
}
