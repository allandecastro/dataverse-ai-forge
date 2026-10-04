namespace DataverseForge.Application.Rag;

/// <summary>
/// Classifies a document source by its epistemic role in the project.
/// The category determines which sources are searched first based on the intent of the query
/// (see <see cref="SearchIntent"/>). Authority is not absolute — it depends on what is being asked.
///
/// <list type="bullet">
///   <item><see cref="Implementation"/> — Dataverse solution exports: ground truth of what IS deployed</item>
///   <item><see cref="Specification"/> — validated specs, functional docs: ground truth of what SHOULD be</item>
///   <item><see cref="ClientProvided"/> — documents received from the client: the client's stated intent</item>
///   <item><see cref="Transcript"/> — meeting recordings / VTT transcripts: what was said</item>
///   <item><see cref="Backlog"/> — AzDo work items: what is planned</item>
///   <item><see cref="Internal"/> — internal notes, architecture docs, miscellaneous</item>
/// </list>
/// </summary>
public enum SourceCategory
{
    Implementation,
    Specification,
    ClientProvided,
    Transcript,
    Backlog,
    Internal,
}
