# Dataverse AI Forge Backend

.NET 9 Clean Architecture solution using Microsoft Agent Framework.

## Solution Structure

```
src/
  DVForge.Domain/          → Entities, enums, interfaces (zero deps)
  DVForge.Application/     → Agents, tools, use cases, DTOs
  DVForge.Infrastructure/  → Dataverse client, GitHub, SQLite, AG-UI emitter
  DVForge.Api/             → ASP.NET host, SSE endpoint, DI registration
tests/
  *.Tests/                 → xUnit + Moq
```

## Commands

```bash
dotnet build DVForge.slnx
dotnet test DVForge.slnx
dotnet run --project src/DVForge.Api
```

## Agent Framework Patterns

Agents use Microsoft.Agents.AI with handoff orchestration:

```csharp
// Agent creation pattern
AIAgent architect = chatClient.AsAIAgent(
    name: "SolutionArchitect",
    instructions: "...",  // From system prompt in spec section 7
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
- Tools follow naming: {plan|execute|get|validate|verify|generate}_{noun}
- Every tool that writes to Dataverse must push to IOperationStack
- Agent system prompts are defined in spec section 7 — use them verbatim
- AG-UI events emitted via IAgentEventEmitter (SSE): TEXT_MESSAGE, TOOL_CALL, STATE_SNAPSHOT
- Use ILogger<T> everywhere, structured logging with OpenTelemetry
- Configuration via IOptions pattern + appsettings.json + user-secrets for keys