using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using DataverseForge.Application.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class IngestDocumentsToolTests
{
    private static RagService BuildRagService(IRagStore store)
    {
        var embMock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        embMock.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>((inputs, _, _) =>
            {
                var results = new GeneratedEmbeddings<Embedding<float>>(
                    inputs.Select(_ => new Embedding<float>(new float[3])).ToList());
                return Task.FromResult(results);
            });
        return new RagService(store, embMock.Object, [], NullLogger<RagService>.Instance);
    }

    [Fact]
    public async Task IngestDocumentsAsync_UnknownSourceType_ReturnsError()
    {
        var storeMock = new Mock<IRagStore>();
        var ragService = BuildRagService(storeMock.Object);
        var factory = new Mock<IDocumentSourceFactory>();

        var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, NullLogger<IngestDocumentsTool>.Instance);
        var result = await tool.IngestDocumentsAsync("unknown-type", "My Source");

        Assert.Contains("Error", result);
        Assert.Contains("sourceType", result);
    }

    [Fact]
    public async Task IngestDocumentsAsync_FilesystemWithNonExistentPath_ReturnsError()
    {
        var storeMock = new Mock<IRagStore>();
        var ragService = BuildRagService(storeMock.Object);
        var factory = new Mock<IDocumentSourceFactory>();

        var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, NullLogger<IngestDocumentsTool>.Instance);
        var result = await tool.IngestDocumentsAsync("filesystem", "Specs", path: "C:/does-not-exist-xyz-test-path");

        Assert.Contains("Error", result);
        Assert.Contains("does not exist", result);
    }

    [Fact]
    public async Task IngestDocumentsAsync_FilesystemWithoutPath_ReturnsError()
    {
        var storeMock = new Mock<IRagStore>();
        var ragService = BuildRagService(storeMock.Object);
        var factory = new Mock<IDocumentSourceFactory>();

        var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, NullLogger<IngestDocumentsTool>.Instance);
        var result = await tool.IngestDocumentsAsync("filesystem", "Specs", path: null);

        Assert.Contains("Error", result);
    }

    [Fact]
    public async Task IngestDocumentsAsync_AzdoWithoutRegisteredSource_ReturnsCredentialHint()
    {
        var storeMock = new Mock<IRagStore>();
        var ragService = BuildRagService(storeMock.Object);
        var factory = new Mock<IDocumentSourceFactory>();

        // No IDocumentSource registered → AzDo source not found
        var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, NullLogger<IngestDocumentsTool>.Instance);
        var result = await tool.IngestDocumentsAsync("azdo", "Backlog AzDo");

        Assert.Contains("Error", result);
        Assert.Contains("Azure DevOps", result);
    }

    [Fact]
    public async Task IngestDocumentsAsync_FilesystemWithExistingPath_CallsFactoryAndReturnsSuccess()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "forge-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var storeMock = new Mock<IRagStore>();
            storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            storeMock.Setup(s => s.GetIndexedFilePathsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            storeMock.Setup(s => s.GetFileLastModifiedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateTimeOffset?)null);
            storeMock.Setup(s => s.UpsertSourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SourceCategory>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            storeMock.Setup(s => s.UpsertChunksAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IReadOnlyList<(string, ReadOnlyMemory<float>)>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            storeMock.Setup(s => s.MarkIngestedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var ragService = BuildRagService(storeMock.Object);
            var factory = new Mock<IDocumentSourceFactory>();

            // Factory creates a source that returns no documents (empty dir)
            var emptySource = new Mock<IDocumentSource>();
            emptySource.Setup(s => s.Id).Returns("fs-test");
            emptySource.Setup(s => s.Name).Returns("Specs");
            emptySource.Setup(s => s.Category).Returns(SourceCategory.Specification);
            emptySource.Setup(s => s.SupportsIncrementalFetch).Returns(false);
            emptySource.Setup(s => s.GetDocumentsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                .Returns(EmptyAsync());

            factory.Setup(f => f.CreateFilesystem(It.IsAny<string>(), "Specs", SourceCategory.Specification, tempDir))
                .Returns(emptySource.Object);

            var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, NullLogger<IngestDocumentsTool>.Instance);
            var result = await tool.IngestDocumentsAsync("filesystem", "Specs", "Specification", tempDir);

            factory.Verify(f => f.CreateFilesystem(It.IsAny<string>(), "Specs", SourceCategory.Specification, tempDir), Times.Once);
            Assert.Contains("Ingestion complete", result);
            Assert.Contains("Specs", result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task IngestDocumentsAsync_CategoryCaseInsensitive_ParsesCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "forge-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var storeMock = new Mock<IRagStore>();
            storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            storeMock.Setup(s => s.GetIndexedFilePathsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            storeMock.Setup(s => s.UpsertSourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SourceCategory>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            storeMock.Setup(s => s.MarkIngestedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var ragService = BuildRagService(storeMock.Object);
            var factory = new Mock<IDocumentSourceFactory>();

            SourceCategory? capturedCategory = null;
            var emptySource = new Mock<IDocumentSource>();
            emptySource.Setup(s => s.Id).Returns("fs-x");
            emptySource.Setup(s => s.Name).Returns("Transcripts");
            emptySource.Setup(s => s.Category).Returns(SourceCategory.Transcript);
            emptySource.Setup(s => s.SupportsIncrementalFetch).Returns(false);
            emptySource.Setup(s => s.GetDocumentsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                .Returns(EmptyAsync());

            factory.Setup(f => f.CreateFilesystem(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SourceCategory>(), It.IsAny<string>()))
                .Callback<string, string, SourceCategory, string>((_, _, cat, _) => capturedCategory = cat)
                .Returns(emptySource.Object);

            var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, NullLogger<IngestDocumentsTool>.Instance);
            await tool.IngestDocumentsAsync("filesystem", "Transcripts", "transcript", tempDir);

            // "transcript" (lowercase) should parse to SourceCategory.Transcript
            Assert.Equal(SourceCategory.Transcript, capturedCategory);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async IAsyncEnumerable<RawDocument> EmptyAsync()
    {
        await Task.CompletedTask;
        yield break;
    }
}
