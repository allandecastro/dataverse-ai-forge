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

        var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, new Mock<IProjectConnectionStore>().Object, new Mock<ISessionContext>().Object, NullLogger<IngestDocumentsTool>.Instance);
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

        var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, new Mock<IProjectConnectionStore>().Object, new Mock<ISessionContext>().Object, NullLogger<IngestDocumentsTool>.Instance);
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

        var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, new Mock<IProjectConnectionStore>().Object, new Mock<ISessionContext>().Object, NullLogger<IngestDocumentsTool>.Instance);
        var result = await tool.IngestDocumentsAsync("filesystem", "Specs", path: null);

        Assert.Contains("Error", result);
    }

    [Fact]
    public async Task IngestDocumentsAsync_AzdoWithoutProjectConnection_ReturnsCredentialHint()
    {
        var storeMock = new Mock<IRagStore>();
        var ragService = BuildRagService(storeMock.Object);
        var factory = new Mock<IDocumentSourceFactory>();

        // The project has no Azure DevOps connection
        var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, new Mock<IProjectConnectionStore>().Object, new Mock<ISessionContext>().Object, NullLogger<IngestDocumentsTool>.Instance);
        var result = await tool.IngestDocumentsAsync("azdo", "Backlog AzDo");

        Assert.Contains("Error", result);
        Assert.Contains("Azure DevOps", result);
        factory.Verify(f => f.CreateAzureDevOps(It.IsAny<AzureDevOpsConnection>()), Times.Never);
    }

    [Fact]
    public async Task IngestDocumentsAsync_Azdo_UsesTheCurrentProjectsConnection()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        storeMock.Setup(s => s.GetIndexedFilePathsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var projectAzdo = new AzureDevOpsConnection("https://dev.azure.com/client-a", "Expenses", "pat-a");
        var connections = new Mock<IProjectConnectionStore>();
        connections.Setup(c => c.GetAsync("project-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectConnection(null, projectAzdo));
        var session = new Mock<ISessionContext>();
        session.Setup(s => s.SessionId).Returns("project-a");

        var azdoSource = new Mock<IDocumentSource>();
        azdoSource.Setup(s => s.Id).Returns("azdo-backlog");
        azdoSource.Setup(s => s.Name).Returns("Backlog AzDo");
        azdoSource.Setup(s => s.GetDocumentsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).Returns(EmptyAsync());
        var factory = new Mock<IDocumentSourceFactory>();
        factory.Setup(f => f.CreateAzureDevOps(projectAzdo)).Returns(azdoSource.Object);

        var tool = new IngestDocumentsTool(storeMock.Object, BuildRagService(storeMock.Object), [], factory.Object,
            connections.Object, session.Object, NullLogger<IngestDocumentsTool>.Instance);
        var result = await tool.IngestDocumentsAsync("azdo", "Backlog", "Backlog");

        Assert.Contains("Ingestion complete", result);
        factory.Verify(f => f.CreateAzureDevOps(projectAzdo), Times.Once);
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

            var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, new Mock<IProjectConnectionStore>().Object, new Mock<ISessionContext>().Object, NullLogger<IngestDocumentsTool>.Instance);
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

            var tool = new IngestDocumentsTool(storeMock.Object, ragService, [], factory.Object, new Mock<IProjectConnectionStore>().Object, new Mock<ISessionContext>().Object, NullLogger<IngestDocumentsTool>.Instance);
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

    [Fact]
    public async Task IngestDocumentsAsync_IngestThrows_ReturnsRealErrorInsteadOfFailing()
    {
        // e.g. the embedding deployment doesn't exist (HTTP 404 DeploymentNotFound). Before,
        // the throw escaped the tool and the agent only saw "Function failed.".
        var tempDir = Path.Combine(Path.GetTempPath(), $"ingest-throw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var storeMock = new Mock<IRagStore>();
            storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            storeMock.Setup(s => s.GetIndexedFilePathsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            storeMock.Setup(s => s.UpsertSourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SourceCategory>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var factory = new Mock<IDocumentSourceFactory>();
            var source = new Mock<IDocumentSource>();
            source.Setup(s => s.Id).Returns("fs-test");
            source.Setup(s => s.Name).Returns("Specs");
            source.Setup(s => s.GetDocumentsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                .Returns(ThrowingAsync(new InvalidOperationException("HTTP 404 (DeploymentNotFound)")));
            factory.Setup(f => f.CreateFilesystem(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SourceCategory>(), tempDir))
                .Returns(source.Object);

            var tool = new IngestDocumentsTool(storeMock.Object, BuildRagService(storeMock.Object), [], factory.Object, new Mock<IProjectConnectionStore>().Object, new Mock<ISessionContext>().Object, NullLogger<IngestDocumentsTool>.Instance);
            var result = await tool.IngestDocumentsAsync("filesystem", "Specs", "Specification", tempDir);

            Assert.Contains("Ingestion error for 'Specs'", result);
            Assert.Contains("DeploymentNotFound", result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

#pragma warning disable CS1998 // async iterator that throws before yielding
    private static async IAsyncEnumerable<RawDocument> ThrowingAsync(Exception ex)
    {
        throw ex;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
#pragma warning restore CS1998

    [Theory]
    [InlineData("~/forge-rag-demo/spec", "forge-rag-demo/spec")]
    [InlineData("~", "")]
    public void ExpandHome_ExpandsLeadingTilde(string input, string relative)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expected = relative.Length == 0 ? home : Path.Combine(home, relative);

        Assert.Equal(expected, IngestDocumentsTool.ExpandHome(input));
    }

    [Theory]
    [InlineData("/abs/path")]
    [InlineData("relative/~not-home")]
    [InlineData("~user/elsewhere")]
    public void ExpandHome_LeavesOtherPathsAlone(string input) =>
        Assert.Equal(input, IngestDocumentsTool.ExpandHome(input));
}
