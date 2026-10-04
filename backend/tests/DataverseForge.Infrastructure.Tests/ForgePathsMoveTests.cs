namespace DataverseForge.Infrastructure.Tests;

/// <summary>
/// "Change location" must only carry the app's own files. The data dir can be a shared
/// folder the user picked (e.g. ~/Documents); moving the folder's whole contents would
/// relocate their unrelated files too.
/// </summary>
public sealed class ForgePathsMoveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"forge-move-{Guid.NewGuid():N}");
    private string Source => Path.Combine(_root, "Documents");
    private string Target => Path.Combine(_root, "NewHome");

    public ForgePathsMoveTests() => Directory.CreateDirectory(Source);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void Write(string relative, string content = "x")
    {
        var path = Path.Combine(Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void MoveAppData_MovesOnlyAppOwnedEntries_LeavesUserFilesInPlace()
    {
        Write("state.db", "db");
        Write("state.db-wal");
        Write("user-settings.json", "{}");
        Write(".secret.key", "key");
        Write("rag/projects/p1/rag.db");
        Write("exports/solution.zip");
        // The user's own stuff in the same folder.
        Write("GitHub/repo/README.md");
        Write("Vidéos/holiday.mp4");
        Write("taxes-2025.pdf");

        ForgePaths.MoveAppData(Source, Target);

        Assert.Equal("db", File.ReadAllText(Path.Combine(Target, "state.db")));
        Assert.True(File.Exists(Path.Combine(Target, "state.db-wal")));
        Assert.True(File.Exists(Path.Combine(Target, "user-settings.json")));
        Assert.True(File.Exists(Path.Combine(Target, ".secret.key")));
        Assert.True(File.Exists(Path.Combine(Target, "rag", "projects", "p1", "rag.db")));
        Assert.True(File.Exists(Path.Combine(Target, "exports", "solution.zip")));

        Assert.False(File.Exists(Path.Combine(Source, "state.db")));
        Assert.False(Directory.Exists(Path.Combine(Source, "rag")));

        Assert.True(File.Exists(Path.Combine(Source, "GitHub", "repo", "README.md")));
        Assert.True(File.Exists(Path.Combine(Source, "Vidéos", "holiday.mp4")));
        Assert.True(File.Exists(Path.Combine(Source, "taxes-2025.pdf")));
        Assert.False(Directory.Exists(Path.Combine(Target, "GitHub")));
        Assert.False(File.Exists(Path.Combine(Target, "taxes-2025.pdf")));
    }

    [Fact]
    public void MoveAppData_MissingEntries_AreSkipped()
    {
        Write("user-settings.json");

        ForgePaths.MoveAppData(Source, Target);

        Assert.True(File.Exists(Path.Combine(Target, "user-settings.json")));
        Assert.False(File.Exists(Path.Combine(Target, "state.db")));
    }

    [Fact]
    public void FindAppData_DetectsExistingInstall_IgnoresUnrelatedFiles()
    {
        Write("notes.txt");
        Assert.Null(ForgePaths.FindAppData(Source));

        Write("state.db");
        Assert.Equal("state.db", ForgePaths.FindAppData(Source));
    }
}
