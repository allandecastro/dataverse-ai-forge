namespace DataverseForge.Domain.Models;

/// <summary>
/// Output of <c>RunSavedQuery</c> — the rows returned by the view's FetchXML
/// plus the metadata the agent needs to interpret the result (which view ran,
/// against which entity, how the FetchXML was shaped). Returning a typed
/// envelope rather than just the rows keeps the agent's reasoning grounded —
/// it can confirm "I ran view X against entity Y" without re-reading state.
/// </summary>
public sealed record SavedQueryRunResult(
    Guid SavedQueryId,
    string ViewName,
    string EntityLogicalName,
    string EntitySetName,
    string FetchXml,
    int RowCount,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);
