using System.Runtime.CompilerServices;

namespace DataverseForge.Infrastructure.Tests;

/// <summary>
/// Isolates the WHOLE test assembly's data dir from the developer's real one.
///
/// The AG-UI smoke tests boot the real host (Program.cs), which resolves
/// <c>ForgePaths.GetWritableDataDir()</c>. Without this, ForgePaths would read
/// the developer's OS config dir and could APPLY a queued in-app "pending move",
/// relocating real user data (projects, encrypted credentials) just because
/// tests ran. Pinning <c>DVFORGE_DATA_DIR</c> to a throwaway temp dir before any
/// ForgePaths access makes tests self-contained (and, since the env pin makes
/// ForgePaths skip the in-app pointer + pending move, safe by construction).
/// </summary>
internal static class TestDataDirInitializer
{
    [ModuleInitializer]
    internal static void Init()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DVFORGE_DATA_DIR")))
            return; // Respect an explicit override (e.g. CI pinning its own dir).

        var dir = Path.Combine(Path.GetTempPath(), "dvforge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("DVFORGE_DATA_DIR", dir);
    }
}
