using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using DataverseForge.Infrastructure.Rag;

namespace DataverseForge.Infrastructure.Tests.Rag;

/// <summary>
/// Unit tests for <see cref="FilesystemDocumentSource"/> using a temporary directory.
/// Tests only .txt / .md extensions to avoid requiring docx/pdf binary files in the test suite.
/// </summary>
public sealed class FilesystemDocumentSourceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ForgeRagTests_" + Guid.NewGuid().ToString("N"));

    public FilesystemDocumentSourceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* Best-effort cleanup */ }
    }

    private FilesystemDocumentSource CreateSource() =>
        new("fs-test", "Test Source", SourceCategory.Internal, _tempDir);

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDocumentsAsync_TxtFile_ReturnsDocument()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "doc.txt"), "Hello world text content");

        var source = CreateSource();
        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Single(docs);
        Assert.Equal("Hello world text content", docs[0].Content);
        Assert.Equal("fs-test", docs[0].SourceId);
    }

    [Fact]
    public async Task GetDocumentsAsync_MdFile_ReturnsDocument()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "readme.md"), "# Title\nSome content");

        var source = CreateSource();
        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Single(docs);
        Assert.Contains("# Title", docs[0].Content);
    }

    [Fact]
    public async Task GetDocumentsAsync_UnsupportedExtension_Skipped()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "data.json"), """{"key":"value"}""");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "script.cs"), "public class Foo {}");

        var source = CreateSource();
        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Empty(docs);
    }

    [Fact]
    public async Task GetDocumentsAsync_MixedExtensions_OnlySupportedReturned()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "valid.txt"), "Valid content");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "invalid.xml"), "<root/>");

        var source = CreateSource();
        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Single(docs);
        Assert.Equal("Valid content", docs[0].Content);
    }

    [Fact]
    public async Task GetDocumentsAsync_EmptyFile_Skipped()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "empty.txt"), "");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "whitespace.md"), "   \n  \t  ");

        var source = CreateSource();
        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Empty(docs);
    }

    [Fact]
    public async Task GetDocumentsAsync_NonExistentDirectory_ReturnsEmpty()
    {
        var source = new FilesystemDocumentSource("fs-x", "X", SourceCategory.Internal,
            Path.Combine(Path.GetTempPath(), "ForgeRagTests_doesnotexist_" + Guid.NewGuid().ToString("N")));

        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Empty(docs);
    }

    [Fact]
    public async Task GetDocumentsAsync_Subdirectories_RecursivelyEnumerated()
    {
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "root.txt"), "Root content");
        await File.WriteAllTextAsync(Path.Combine(subDir, "sub.txt"), "Sub content");

        var source = CreateSource();
        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Equal(2, docs.Count);
        Assert.Contains(docs, d => d.Content == "Root content");
        Assert.Contains(docs, d => d.Content == "Sub content");
    }

    [Fact]
    public async Task GetDocumentsAsync_Document_HasCorrectLastModified()
    {
        var path = Path.Combine(_tempDir, "doc.txt");
        await File.WriteAllTextAsync(path, "Content");
        var expectedLastModified = File.GetLastWriteTimeUtc(path);

        var source = CreateSource();
        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Single(docs);
        Assert.Equal(expectedLastModified, docs[0].LastModified.UtcDateTime, precision: TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetDocumentsAsync_Document_FilenameIsFilePath()
    {
        var path = Path.Combine(_tempDir, "myfile.txt");
        await File.WriteAllTextAsync(path, "Content");

        var source = CreateSource();
        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Single(docs);
        Assert.Equal(path, docs[0].FilePath);
    }

    [Fact]
    public async Task GetDocumentsAsync_SourceId_MatchesConstructorId()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "doc.txt"), "Content");

        var source = new FilesystemDocumentSource("my-custom-id", "My Source", SourceCategory.Backlog, _tempDir);
        var docs = await source.GetDocumentsAsync().ToListAsync();

        Assert.Single(docs);
        Assert.Equal("my-custom-id", docs[0].SourceId);
    }
}
