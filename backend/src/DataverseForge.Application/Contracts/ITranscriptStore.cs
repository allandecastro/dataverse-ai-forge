namespace DataverseForge.Application.Contracts;

/// <summary>
/// Persists the frontend chat transcript — the rendered <c>ForgeMessage[]</c> as
/// an opaque JSON string — per session, so history survives a backend restart
/// and is available across browsers/devices. Opaque by design: the backend never
/// parses the shape; the frontend owns it. This is separate from the LLM
/// conversation history (<see cref="IConversationStore"/>), which only holds
/// role+text for feeding the model.
/// </summary>
public interface ITranscriptStore
{
    /// <summary>Returns the stored transcript JSON, or <c>"[]"</c> if none.</summary>
    Task<string> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    Task SaveAsync(string sessionId, string transcriptJson, CancellationToken cancellationToken = default);
}
