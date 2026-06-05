using DataverseForge.Application.Dtos;
using Microsoft.Extensions.AI;

namespace DataverseForge.Application.Contracts;

public interface IAgent
{
    string Name { get; }
    IReadOnlyList<string> HandoffTargets { get; }

    /// <summary>
    /// The agent's raw system-prompt template (still containing the
    /// <c>[INJECTED AT RUNTIME]</c> placeholder). The orchestrator injects the
    /// live <c>ProjectState</c> JSON before sending it to the model. Exposed on
    /// the interface so the streaming orchestrator can read it directly instead
    /// of hardcoding a name→prompt map.
    /// </summary>
    string SystemPrompt { get; }

    /// <summary>
    /// The tools available to this agent. Exposed so the orchestrator can read
    /// them directly instead of reflecting over the primary-constructor field.
    /// </summary>
    IReadOnlyList<AITool> Tools { get; }

    Task<Dtos.ChatResponse> ChatAsync(string sessionId, string message, CancellationToken cancellationToken = default);
}
