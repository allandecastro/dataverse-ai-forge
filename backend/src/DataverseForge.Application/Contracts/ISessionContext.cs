namespace DataverseForge.Application.Contracts;

public interface ISessionContext
{
    string SessionId { get; set; }

    /// <summary>
    /// Per-turn correlation ID. Set by the orchestrator at the start of each user
    /// message and propagated through structured logs in tools and client calls so a
    /// single user turn can be traced end-to-end across all log sinks.
    /// </summary>
    string RunId { get; set; }
}
