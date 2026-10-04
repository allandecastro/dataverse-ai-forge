namespace DataverseForge.Application.Rag;

/// <summary>
/// Defines named search intents that control the order in which source categories are searched.
///
/// <para>Authority is not absolute: forge exports are the source of truth for <em>what is deployed</em>,
/// but specifications are the source of truth for <em>what should be</em> (e.g., when analysing a bug).</para>
///
/// <para>Pass one of the string constants as the <c>intent</c> parameter to
/// <c>search_context</c>. If omitted, all categories are searched simultaneously.</para>
/// </summary>
public static class SearchIntent
{
    /// <summary>
    /// What is currently deployed / how a feature actually behaves.
    /// Order: Implementation → Specification → ClientProvided → Transcript → Backlog → Internal
    /// </summary>
    public const string ImplementedBehavior = "implemented-behavior";

    /// <summary>
    /// What the expected / specified behavior should be — use this when analysing a bug
    /// or verifying a requirement.
    /// Order: Specification → ClientProvided → Transcript → Backlog → Implementation → Internal
    /// </summary>
    public const string ExpectedBehavior = "expected-behavior";

    /// <summary>
    /// What is planned for upcoming iterations.
    /// Order: Backlog → Specification → ClientProvided → Transcript → Implementation → Internal
    /// </summary>
    public const string Planning = "planning";

    /// <summary>
    /// General project context, client intent, or meeting history.
    /// Order: ClientProvided → Transcript → Specification → Backlog → Implementation → Internal
    /// </summary>
    public const string ProjectContext = "context";

    /// <summary>
    /// Returns the ordered list of <see cref="SourceCategory"/> values for the given intent.
    /// Categories earlier in the list are searched first (cascading).
    /// Returns <c>null</c> for unknown / null intent, meaning all sources are searched at once.
    /// </summary>
    public static IReadOnlyList<SourceCategory>? GetCategoryOrder(string? intent) =>
        intent?.Trim().ToLowerInvariant() switch
        {
            ImplementedBehavior =>
            [
                SourceCategory.Implementation,
                SourceCategory.Specification,
                SourceCategory.ClientProvided,
                SourceCategory.Transcript,
                SourceCategory.Backlog,
                SourceCategory.Internal,
            ],
            ExpectedBehavior =>
            [
                SourceCategory.Specification,
                SourceCategory.ClientProvided,
                SourceCategory.Transcript,
                SourceCategory.Backlog,
                SourceCategory.Implementation,
                SourceCategory.Internal,
            ],
            Planning =>
            [
                SourceCategory.Backlog,
                SourceCategory.Specification,
                SourceCategory.ClientProvided,
                SourceCategory.Transcript,
                SourceCategory.Implementation,
                SourceCategory.Internal,
            ],
            ProjectContext =>
            [
                SourceCategory.ClientProvided,
                SourceCategory.Transcript,
                SourceCategory.Specification,
                SourceCategory.Backlog,
                SourceCategory.Implementation,
                SourceCategory.Internal,
            ],
            _ => null, // general / unknown: search all at once
        };
}
