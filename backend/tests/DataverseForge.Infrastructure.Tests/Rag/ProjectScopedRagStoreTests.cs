using DataverseForge.Application.Rag;
using DataverseForge.Infrastructure.Rag;
using DataverseForge.Infrastructure.Stores;

namespace DataverseForge.Infrastructure.Tests.Rag;

/// <summary>
/// Per-project isolation of the RAG knowledge base: each project (session) gets its own
/// database, so a source ingested in one project is never visible from another.
/// </summary>
public sealed class ProjectScopedRagStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"forge-rag-projects-{Guid.NewGuid():N}");
    private readonly RagStoreProvider _provider;

    public ProjectScopedRagStoreTests() => _provider = new RagStoreProvider(_root);

    public void Dispose()
    {
        foreach (var dir in Directory.Exists(_root) ? Directory.GetDirectories(_root) : [])
            _provider.DeleteProject(Path.GetFileName(dir));
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ProjectScopedRagStore StoreFor(string sessionId) =>
        new(_provider, new SessionContext { SessionId = sessionId });

    private static ReadOnlyMemory<float> Vec(params float[] values) => values.AsMemory();

    [Fact]
    public async Task SourceIngestedInOneProject_IsInvisibleFromAnother()
    {
        var projectA = StoreFor("project-a");
        var projectB = StoreFor("project-b");

        await projectA.UpsertSourceAsync("specs", "Specs", "filesystem", "/a", SourceCategory.Internal);
        await projectA.UpsertChunksAsync("specs", "/a/spec.md", DateTimeOffset.UtcNow,
            [("Invoices must be validated", Vec(1f, 0f))]);

        Assert.Single(await projectA.GetSourcesAsync());
        Assert.Empty(await projectB.GetSourcesAsync());
        Assert.Empty(await projectB.SearchAsync(Vec(1f, 0f), topK: 5));
        Assert.Empty(await projectB.GetAllChunksAsync("specs"));
    }

    [Fact]
    public async Task SameSourceId_InTwoProjects_KeepsSeparateContent()
    {
        var projectA = StoreFor("project-a");
        var projectB = StoreFor("project-b");

        await projectA.UpsertSourceAsync("backlog", "Backlog", "azdo", null, SourceCategory.Internal);
        await projectA.UpsertChunksAsync("backlog", "/wi.txt", DateTimeOffset.UtcNow, [("A content", Vec(1f, 0f))]);
        await projectB.UpsertSourceAsync("backlog", "Backlog", "azdo", null, SourceCategory.Internal);
        await projectB.UpsertChunksAsync("backlog", "/wi.txt", DateTimeOffset.UtcNow, [("B content", Vec(1f, 0f))]);

        Assert.Equal("A content", Assert.Single(await projectA.GetAllChunksAsync("backlog")).Content);
        Assert.Equal("B content", Assert.Single(await projectB.GetAllChunksAsync("backlog")).Content);
    }

    [Fact]
    public async Task DeleteProject_RemovesItsKnowledgeBase_AndLeavesOthers()
    {
        var projectA = StoreFor("project-a");
        var projectB = StoreFor("project-b");
        await projectA.UpsertSourceAsync("specs", "Specs", "filesystem", "/a", SourceCategory.Internal);
        await projectB.UpsertSourceAsync("specs", "Specs", "filesystem", "/b", SourceCategory.Internal);

        _provider.DeleteProject("project-a");

        Assert.False(Directory.Exists(Path.Combine(_root, "project-a")));
        Assert.Empty(await StoreFor("project-a").GetSourcesAsync());
        Assert.Single(await projectB.GetSourcesAsync());
    }

    [Fact]
    public async Task UnsafeSessionId_IsNotUsedAsAPathSegment()
    {
        var store = StoreFor("../../escape");

        await store.UpsertSourceAsync("specs", "Specs", "filesystem", "/x", SourceCategory.Internal);

        // Everything stays under the root, in a hashed folder.
        var dir = Assert.Single(Directory.GetDirectories(_root));
        Assert.StartsWith("h_", Path.GetFileName(dir));
        Assert.False(Directory.Exists(Path.GetFullPath(Path.Combine(_root, "../../escape"))));
    }

    [Fact]
    public async Task NoActiveSession_Throws_InsteadOfFallingBackToASharedStore()
    {
        var store = StoreFor(string.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetSourcesAsync());
    }
}
