# Dataverse AI Forge Backend

.NET 10 Clean Architecture solution using Microsoft Agent Framework.

## Solution Structure

```
src/
  DataverseForge.Domain/          → Entities, enums, interfaces (zero deps)
  DataverseForge.Application/     → Agents, tools, use cases, DTOs
  DataverseForge.Infrastructure/  → Dataverse client, in-memory state stores, AG-UI emitter
  DataverseForge.Api/             → ASP.NET host, SSE endpoint, DI registration
tests/
  *.Tests/                 → xUnit + Moq
```

## Commands

```bash
dotnet build DataverseForge.slnx
dotnet test DataverseForge.slnx
dotnet run --project src/DataverseForge.Api
```

## Agent Framework Patterns

Agents use Microsoft.Agents.AI with handoff orchestration:

```csharp
// Agent creation pattern
AIAgent architect = chatClient.AsAIAgent(
    name: "SolutionArchitect",
    instructions: "...",  // From Agents/Prompts/SolutionArchitect.md (see Agent system prompts below)
);
architect.AddTool(new GetEnvironmentInfoTool(dataverseClient));
```

Tools are implemented as classes with `[AIFunction]` attribute:

```csharp
public class GetEnvironmentInfoTool(IDataverseClient client)
{
    [AIFunction("Get Dataverse environment info")]
    public async Task<EnvironmentInfo> GetEnvironmentInfoAsync()
    {
        return await client.GetEnvironmentInfoAsync();
    }
}
```

## Key Rules

- Domain project has ZERO NuGet references (pure C#)
- All Dataverse HTTP calls go through IDataverseClient in Infrastructure
- Tools follow PascalCase verb-noun naming. Actual verb set in use:
  `Plan*`, `Execute*` (the standard plan/execute split for write ops),
  `Get*` (read), `Validate*` / `Check*` / `Analyze*` (read-only analysis),
  `Verify*` (post-execution verification by Tester),
  `Generate*` (Documentor outputs), `Handoff*` (agent routing),
  `Request*` (user-interaction tools like RequestApproval),
  `Create*` / `Update*` / `Select*` / `Run*` / `Push*` / `Undo*` (direct ops where a plan/execute split would be overkill — solutions, publishers, undo, etc.).
  When adding a new tool, prefer Plan/Execute for any Dataverse write that benefits from approval; only use the direct verbs above when the operation is trivially safe or already idempotent.
- Every tool that writes to Dataverse must push to IOperationStack
- Agent system prompts live in `src/DataverseForge.Application/Agents/Prompts/*.md` (embedded markdown), composed with shared `CommonPromptFragments` by each `*Prompt.cs`. Prompt sizes are budgeted by `PromptSizeBudgetTests`
- AG-UI events emitted via IAgentEventEmitter (SSE): TEXT_MESSAGE, TOOL_CALL, STATE_SNAPSHOT
- Use ILogger<T> everywhere with structured message templates (no string interpolation in log calls)
- Configuration via IOptions pattern + appsettings.json + user-secrets for keys