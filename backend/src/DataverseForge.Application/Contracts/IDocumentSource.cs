using DataverseForge.Application.Rag;

namespace DataverseForge.Application.Contracts;

/// <summary>
/// Represents a categorized source of documents that can be ingested into the RAG store.
/// Implementations cover filesystem directories, Dataverse solution exports, AzDo work items,
/// and Git repositories.
///
/// <para>The <see cref="Category"/> declares the epistemic role of this source (what kind of truth it
/// carries). The cascading search order is then determined at query time by the <see cref="SearchIntent"/>
/// of the question — for example, <see cref="SourceCategory.Implementation"/> sources are searched first
/// when asking "what is deployed", but <see cref="SourceCategory.Specification"/> sources are searched
/// first when asking "what should the behavior be" (bug analysis).</para>
///
/// <para>Sources that support incremental fetch (e.g. AzDo, Git) should set
/// <see cref="SupportsIncrementalFetch"/> to <c>true</c> and use the <c>changedSince</c> parameter
/// in <see cref="GetDocumentsAsync"/> to return only documents modified after that date.
/// File-system-based sources should leave it <c>false</c> — they enumerate all files and rely on
/// <c>IRagStore.GetFileLastModifiedAsync</c> for skip logic.</para>
/// </summary>
public interface IDocumentSource
{
    /// <summary>Stable identifier for this source (e.g. "specs", "transcripts", "forge-exports", "azdo").</summary>
    string Id { get; }

    /// <summary>Human-readable label shown to users (e.g. "Specs validées", "Backlog AzDo").</summary>
    string Name { get; }

    /// <summary>
    /// Epistemic category of this source. Controls which sources are searched first depending
    /// on the intent of the query (see <see cref="SearchIntent"/>).
    /// </summary>
    SourceCategory Category { get; }

    /// <summary>
    /// When <c>true</c>, the source can perform a diff-based fetch: <see cref="GetDocumentsAsync"/>
    /// receives the last ingest timestamp and returns only documents created or modified after that
    /// date. The pruning of deleted documents is skipped for incremental sources.
    /// When <c>false</c>, <see cref="GetDocumentsAsync"/> always enumerates all documents and the
    /// store handles skip logic via file timestamps.
    /// </summary>
    bool SupportsIncrementalFetch { get; }

    /// <summary>
    /// Enumerate documents available in this source.
    /// <paramref name="changedSince"/> is non-null only when <see cref="SupportsIncrementalFetch"/>
    /// is <c>true</c> and a previous ingest has been recorded — implementations should then return
    /// only documents modified after that timestamp.
    /// </summary>
    IAsyncEnumerable<RawDocument> GetDocumentsAsync(DateTimeOffset? changedSince = null, CancellationToken cancellationToken = default);
}
