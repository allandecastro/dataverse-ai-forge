# Dataverse AI Forge — Implementation Guide for Claude Code

> How to build Dataverse AI Forge using Claude Code with a Visual Studio .slnx solution.
> This document contains: solution structure, CLAUDE.md files, custom commands,
> phase-by-phase prompts, and workflow best practices.

---

## Table of Contents

1. [Strategy: How to Use Claude Effectively](#1-strategy)
2. [Repository Structure](#2-repository-structure)
3. [.slnx Solution File](#3-slnx-solution-file)
4. [CLAUDE.md Files (Hierarchical)](#4-claudemd-files)
5. [Custom Commands (.claude/commands)](#5-custom-commands)
6. [Phase-by-Phase Prompts](#6-phase-by-phase-prompts)
7. [Workflow Tips & Anti-Patterns](#7-workflow-tips)

---

## 1. Strategy

### The Core Principle

Claude Code works best when you treat it like a **senior developer who just joined your team**. It's fast, capable, and can hold a lot of context — but it needs:

- **Clear architecture upfront** (the spec)
- **One task at a time** (not "build the whole app")
- **Verification after each step** (build, test, run)
- **Context management** (don't let it drift)

### The Approach

```
1. Monorepo — backend + frontend in one repo (Claude can see everything)
2. .slnx — modern, XML-based, merge-friendly
3. Hierarchical CLAUDE.md — root, /backend, /frontend each get one
4. Spec as source of truth — the master-spec-v4.md lives in /docs
5. Phase-by-phase — one phase per focused session
6. Plan-then-execute — always ask Claude to plan first, review, then code
7. Commit often — after each working unit, commit via Claude
```

### Session Strategy

```
Session 1:  Scaffold solution structure + compile
Session 2:  Dataverse auth + HttpClient
Session 3:  Solution Architect agent (first agent)
Session 4:  AG-UI SSE endpoint
Session 5:  Frontend scaffold + ForgeChatProvider
Session 6:  Chat panel (custom SSE with useForgeChat)
...and so on per the roadmap phases
```

Each session: `/clear` at the start, give Claude the specific task, let it plan, review, execute, test, commit. Keep sessions focused — **one feature per session**.

---

## 2. Repository Structure

```
dataverse-ai-forge/
├── .claude/
│   └── commands/                    # Custom Claude Code commands
│       ├── new-agent.md             # Scaffold a new agent
│       ├── new-tool.md              # Scaffold a new tool for an agent
│       ├── add-project.md           # Add a new .csproj to the solution
│       └── check-health.md          # Build + test + lint
│
├── CLAUDE.md                        # Root — project overview, conventions
│
├── docs/
│   ├── dataverse-ai-forge-spec-v4.md   # THE spec (source of truth)
│   ├── phase-checklist.md               # Living progress tracker
│   └── decisions/                       # Architecture Decision Records
│       └── 001-handoff-over-magentic.md
│
├── backend/
│   ├── CLAUDE.md                    # Backend-specific context
│   ├── DataverseForge.slnx               # .slnx solution file
│   ├── Directory.Build.props         # Shared MSBuild properties
│   ├── Directory.Packages.props      # Central package management
│   ├── global.json                   # SDK version pin
│   │
│   ├── src/
│   │   ├── DataverseForge.Domain/             # Domain entities, enums, interfaces
│   │   │   └── DataverseForge.Domain.csproj
│   │   │
│   │   ├── DataverseForge.Application/        # Use cases, agent definitions, tools
│   │   │   └── DataverseForge.Application.csproj
│   │   │
│   │   ├── DataverseForge.Infrastructure/     # Dataverse client, GitHub client, SQLite
│   │   │   └── DataverseForge.Infrastructure.csproj
│   │   │
│   │   └── DataverseForge.Api/               # ASP.NET host, AG-UI endpoint, middleware
│   │       └── DataverseForge.Api.csproj
│   │
│   └── tests/
│       ├── DataverseForge.Domain.Tests/
│       ├── DataverseForge.Application.Tests/
│       └── DataverseForge.Infrastructure.Tests/
│
├── frontend/
│   ├── CLAUDE.md                    # Frontend-specific context
│   ├── package.json
│   ├── next.config.ts
│   ├── tailwind.config.ts
│   ├── tsconfig.json
│   │
│   ├── src/
│   │   ├── app/
│   │   │   ├── providers.tsx        # ForgeChatProvider + ThemeProvider
│   │   │   ├── page.tsx             # Main layout
│   │   │   └── api/chat/
│   │   │       └── route.ts         # Thin SSE proxy → backend
│   │   │
│   │   ├── components/
│   │   │   ├── layout/
│   │   │   ├── chat/
│   │   │   ├── diagnostics/
│   │   │   ├── workspace/
│   │   │   └── primitives/
│   │   │
│   │   ├── hooks/
│   │   ├── lib/
│   │   └── styles/
│   │
│   └── public/
│
├── docker-compose.yml
├── .gitignore
├── .editorconfig
└── README.md
```

---

## 3. .slnx Solution File

The `.slnx` format is XML-based, human-readable, and merge-friendly. Here's the target structure:

```xml
<Solution>
  <Folder Name="/src">
    <Project Path="src/DataverseForge.Domain/DataverseForge.Domain.csproj" />
    <Project Path="src/DataverseForge.Application/DataverseForge.Application.csproj" />
    <Project Path="src/DataverseForge.Infrastructure/DataverseForge.Infrastructure.csproj" />
    <Project Path="src/DataverseForge.Api/DataverseForge.Api.csproj" />
  </Folder>
  <Folder Name="/tests">
    <Project Path="tests/DataverseForge.Domain.Tests/DataverseForge.Domain.Tests.csproj" />
    <Project Path="tests/DataverseForge.Application.Tests/DataverseForge.Application.Tests.csproj" />
    <Project Path="tests/DataverseForge.Infrastructure.Tests/DataverseForge.Infrastructure.Tests.csproj" />
  </Folder>
</Solution>
```

### global.json

```json
{
  "sdk": {
    "version": "9.0.200",
    "rollForward": "latestFeature"
  }
}
```

### Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

### Directory.Packages.props (Central Package Management)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Microsoft Agent Framework -->
    <PackageVersion Include="Microsoft.Agents.AI" Version="1.0.0-preview.*" />
    <PackageVersion Include="Microsoft.Agents.AI.OpenAI" Version="1.0.0-preview.*" />
    <PackageVersion Include="Microsoft.Agents.AI.Workflows" Version="1.0.0-preview.*" />
    <PackageVersion Include="Microsoft.Agents.AI.Hosting" Version="1.0.0-preview.*" />

    <!-- Azure OpenAI -->
    <PackageVersion Include="Azure.AI.OpenAI" Version="2.*" />
    <PackageVersion Include="Azure.Identity" Version="1.*" />

    <!-- ASP.NET -->
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="9.*" />

    <!-- Persistence -->
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.*" />

    <!-- GitHub -->
    <PackageVersion Include="Octokit" Version="13.*" />

    <!-- Observability -->
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />

    <!-- Testing -->
    <PackageVersion Include="xunit" Version="2.*" />
    <PackageVersion Include="Moq" Version="4.*" />
  </ItemGroup>
</Project>
```

### Creating the .slnx with CLI

```bash
# Scaffold from scratch
cd backend
dotnet new sln --name DataverseForge    # Creates DataverseForge.slnx (default in SDK 9.0.200+)

# Or if it creates .sln, migrate:
dotnet sln migrate

# Add projects
dotnet sln add src/DataverseForge.Domain/DataverseForge.Domain.csproj --solution-folder src
dotnet sln add src/DataverseForge.Application/DataverseForge.Application.csproj --solution-folder src
dotnet sln add src/DataverseForge.Infrastructure/DataverseForge.Infrastructure.csproj --solution-folder src
dotnet sln add src/DataverseForge.Api/DataverseForge.Api.csproj --solution-folder src
dotnet sln add tests/DataverseForge.Domain.Tests/DataverseForge.Domain.Tests.csproj --solution-folder tests
```

---

## 4. CLAUDE.md Files

### 4.1 Root CLAUDE.md (`/CLAUDE.md`)

```markdown
# Dataverse AI Forge

AI-powered platform that builds Microsoft Dataverse solutions through natural language conversation. 10 specialized agents collaborate to design, validate, create, test, document, and secure solutions.

## Architecture

- Backend: .NET 10 Clean Architecture in `/backend` (DataverseForge.slnx)
- Frontend: Next.js + Custom SSE Client in `/frontend`
- Bridge: AG-UI protocol (SSE) between backend and frontend
- Agents: Microsoft Agent Framework with handoff orchestration
- LLM: Azure OpenAI (GPT-4o + GPT-4o-mini)
- Spec: Full specification in `/docs/dataverse-ai-forge-spec-v4.md`

## Build & Test

```bash
# Backend
cd backend && dotnet build DataverseForge.slnx && dotnet test DataverseForge.slnx

# Frontend
cd frontend && npm install && npm run build && npm run lint

# Both (docker)
docker-compose up --build
```

## Key Conventions

- Always read the spec in `/docs/dataverse-ai-forge-spec-v4.md` before implementing a feature
- Backend follows Clean Architecture: Domain has zero dependencies, Application references Domain only, Infrastructure implements interfaces
- Every agent tool follows the plan_*/execute_* split pattern
- Every write operation requires an entry in the operation stack for undo support
- Use central package management (Directory.Packages.props) — never pin versions in individual .csproj
- Commit after each working unit with conventional commits: `feat:`, `fix:`, `refactor:`, `docs:`

## Progress

See `/docs/phase-checklist.md` for current implementation status.
```

### 4.2 Backend CLAUDE.md (`/backend/CLAUDE.md`)

```markdown
# Dataverse AI Forge Backend

.NET 10 Clean Architecture solution using Microsoft Agent Framework.

## Solution Structure

```
src/
  DataverseForge.Domain/          → Entities, enums, interfaces (zero deps)
  DataverseForge.Application/     → Agents, tools, use cases, DTOs
  DataverseForge.Infrastructure/  → Dataverse client, GitHub, SQLite, AG-UI emitter
  DataverseForge.Api/             → ASP.NET host, SSE endpoint, DI registration
tests/
  *.Tests/                   → xUnit + Moq
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
```

### 4.3 Frontend CLAUDE.md (`/frontend/CLAUDE.md`)

```markdown
# Dataverse AI Forge Frontend

Next.js + React + Custom SSE Client + Tailwind CSS.

## Commands

```bash
npm run dev          # Dev server on :3000
npm run build        # Production build
npm run lint         # ESLint
npm run typecheck    # tsc --noEmit
```

## Architecture

All chat communication uses a custom SSE client (ForgeChatProvider + useForgeChat). No third-party chat frameworks. All UI is custom.

Key hooks:
- `useForgeChatContext()` → powers ChatPanel (messages, streaming, send)
- `useDiagnostics()` → derives DiagnosticEntry[] from SSE TOOL_CALL events
- `useForgeApprovals()` → detects request_approval tool calls, renders ApprovalCard
- `useAgent()` → shared orchestrator state (phase, tables, current agent)
- `useTokenTracker()` → accumulates per-agent token/cost data

## Design System

- Dark theme default (NeuralForge aesthetic), light theme via toggle
- Fonts: Geist (UI), JetBrains Mono (code/logical names)
- Tokens defined in `lib/themes.ts` — see spec section 10 for full values
- No emojis in UI — Lucide React icons only
- Each agent has unique icon + color (see `lib/agents.ts`)

## Component Rules

- All components are TypeScript (.tsx), functional, with explicit props interfaces
- Use Tailwind utility classes, theme tokens via CSS variables
- No inline styles except dynamic values (widths, positions)
- Primitives in `/components/primitives/` — reuse Pill, AgentDot, IconBtn, CodeBlock
- File naming: PascalCase for components, camelCase for hooks and utils
```

---

## 5. Custom Commands

Create these in `.claude/commands/` to automate repetitive scaffolding.

### `.claude/commands/new-agent.md`

```markdown
Create a new Dataverse AI Forge agent. Arguments: $ARGUMENTS (agent name)

1. Read the agent definition from `/docs/dataverse-ai-forge-spec-v4.md` section 2 (Agent Roster) and section 7 (System Prompts) for the agent named "$ARGUMENTS"
2. In `backend/src/DataverseForge.Application/Agents/`, create a new file `{AgentName}Agent.cs`
3. Use the Microsoft Agent Framework pattern: create with `chatClient.AsAIAgent()` using the system prompt from the spec
4. Set the model based on spec section 3 (GPT-4o or GPT-4o-mini)
5. Register the agent in DI in `DataverseForge.Api/Registration/AgentRegistration.cs`
6. Add to the handoff topology following spec section 4
7. Create a unit test in `tests/DataverseForge.Application.Tests/Agents/{AgentName}AgentTests.cs`
8. Run `dotnet build backend/DataverseForge.slnx` to verify compilation
9. Commit: `feat: add {AgentName} agent`
```

### `.claude/commands/new-tool.md`

```markdown
Create a new agent tool. Arguments: $ARGUMENTS (tool name, e.g. "get_tables_list")

1. Read the tool definition from `/docs/dataverse-ai-forge-spec-v4.md` section 6 (Tool Catalog) for "$ARGUMENTS"
2. Identify which agent owns this tool from the catalog
3. In `backend/src/DataverseForge.Application/Tools/{AgentFolder}/`, create `{ToolName}Tool.cs`
4. Implement as a class with an `[AIFunction]` method matching the spec signature
5. If it's a write operation (execute_*), inject IOperationStack and push the operation
6. If it calls Dataverse, use IDataverseClient from Infrastructure
7. Wire the tool to its agent in `AgentRegistration.cs`
8. Create a unit test in `tests/DataverseForge.Application.Tests/Tools/{ToolName}ToolTests.cs`
9. Run `dotnet build backend/DataverseForge.slnx && dotnet test backend/DataverseForge.slnx`
10. Commit: `feat: add {tool_name} tool for {AgentName}`
```

### `.claude/commands/add-project.md`

```markdown
Add a new .csproj to the solution. Arguments: $ARGUMENTS (project path relative to backend/)

1. Create the project: `dotnet new classlib -o backend/$ARGUMENTS` (or webapi if it's the Api project)
2. Add to solution: `cd backend && dotnet sln DataverseForge.slnx add $ARGUMENTS`
3. Ensure it uses the shared Directory.Build.props (no TargetFramework in .csproj)
4. Add project references as needed following Clean Architecture rules
5. Run `dotnet build backend/DataverseForge.slnx`
6. Commit: `feat: add $ARGUMENTS project`
```

### `.claude/commands/check-health.md`

```markdown
Run full health check on the project.

1. `cd backend && dotnet build DataverseForge.slnx 2>&1`
2. `cd backend && dotnet test DataverseForge.slnx --no-build 2>&1`
3. `cd frontend && npm run typecheck 2>&1`
4. `cd frontend && npm run lint 2>&1`
5. Report: number of build warnings, test results, lint issues
6. If anything fails, suggest fixes but don't apply them
```

---

## 6. Phase-by-Phase Prompts

### How to Use These Prompts

1. Start a fresh Claude Code session (`/clear` or new terminal)
2. Copy the prompt for the current phase
3. Let Claude plan first (it will read the spec)
4. Review the plan, adjust if needed
5. Let Claude execute
6. Verify: build, run, test
7. Commit and move to next session

---

### Phase 0A — Scaffold Backend Solution

```
Read the spec at docs/dataverse-ai-forge-spec-v4.md, specifically sections 1 (Vision & Stack)
and 10 (Frontend Architecture).

Scaffold the .NET 10 backend solution using .slnx format:

1. Create backend/ directory with global.json (pin SDK 9.0.200), Directory.Build.props
   (net9.0, nullable, implicit usings), and Directory.Packages.props (central package management)
2. Create 4 projects following Clean Architecture:
   - src/DataverseForge.Domain (classlib) — zero dependencies
   - src/DataverseForge.Application (classlib) — references Domain
   - src/DataverseForge.Infrastructure (classlib) — references Application
   - src/DataverseForge.Api (webapi) — references Infrastructure + Application
3. Create test projects:
   - tests/DataverseForge.Domain.Tests
   - tests/DataverseForge.Application.Tests
   - tests/DataverseForge.Infrastructure.Tests
4. Create DataverseForge.slnx with solution folders /src and /tests
5. Add NuGet packages per Directory.Packages.props:
   - Microsoft.Agents.AI, Microsoft.Agents.AI.OpenAI, Microsoft.Agents.AI.Workflows (prerelease)
   - Azure.AI.OpenAI, Azure.Identity
   - Microsoft.EntityFrameworkCore.Sqlite
   - OpenTelemetry.Extensions.Hosting
   - xunit, Moq for test projects
6. In DataverseForge.Domain, create: IDataverseClient interface, EnvironmentInfo record,
   SolutionInfo record, ProjectState class
7. In DataverseForge.Api, set up minimal Program.cs with basic health check endpoint
8. Make sure `dotnet build backend/DataverseForge.slnx` succeeds with zero warnings
9. Commit: "feat: scaffold .NET 10 backend solution with Clean Architecture"

Plan first, show me the file list, then execute.
```

### Phase 0B — Dataverse Authentication

```
Read the spec at docs/dataverse-ai-forge-spec-v4.md, section 1 (Tech Stack) and section 5
(Solution Onboarding).

Implement Dataverse MSAL authentication:

1. In DataverseForge.Infrastructure, create DataverseClient implementing IDataverseClient
2. Use MSAL (Azure.Identity) with ClientSecretCredential or DefaultAzureCredential
3. Configuration via IOptions<DataverseOptions> with:
   - EnvironmentUrl (e.g., https://contoso-dev.crm4.dynamics.com)
   - TenantId, ClientId, ClientSecret
4. Implement GetEnvironmentInfoAsync() — call GET /api/data/v9.2/WhoAmI
   and GET /api/data/v9.2/organizations, return EnvironmentInfo record
5. Register in DI with AddDataverseClient() extension method
6. Add appsettings.json structure + user-secrets for credentials
7. Write integration test that verifies auth works (can be skipped in CI)
8. Verify: `dotnet build && dotnet test`
9. Commit: "feat: add Dataverse MSAL authentication"

Plan first, then execute.
```

### Phase 0C — First Agent (Solution Architect)

```
Read the spec at docs/dataverse-ai-forge-spec-v4.md:
- Section 2: Agent Roster (Solution Architect row)
- Section 3: Model Strategy (GPT-4o for Architect)
- Section 6.1: Solution Architect tools (start with get_environment_info, get_solutions_list)
- Section 7.1: Solution Architect system prompt

Implement the Solution Architect as the first agent:

1. In DataverseForge.Application/Agents/, create SolutionArchitectAgent.cs
   - Use Microsoft Agent Framework: chatClient.AsAIAgent()
   - Use the system prompt from spec section 7.1 (verbatim)
   - Model: GPT-4o
2. Create two tools:
   - Tools/Architect/GetEnvironmentInfoTool.cs — wraps IDataverseClient.GetEnvironmentInfoAsync
   - Tools/Architect/GetSolutionsListTool.cs — calls GET /api/data/v9.2/solutions
   with $select and $filter for unmanaged solutions
3. Register agent + tools in DI
4. Create a simple HTTP endpoint POST /api/chat that:
   - Accepts { message: string }
   - Runs the agent
   - Returns the response as JSON
5. Write unit tests mocking the Dataverse client
6. Verify the agent responds to "What environment am I connected to?"
7. Commit: "feat: add Solution Architect agent with environment + solutions tools"

Plan the file structure first, then implement.
```

### Phase 0D — AG-UI SSE Endpoint

```
Read the spec at docs/dataverse-ai-forge-spec-v4.md:
- Section 11: Frontend SSE Integration (AG-UI Event Mapping table)

Replace the simple POST /api/chat with an AG-UI protocol SSE endpoint:

1. In DataverseForge.Infrastructure, create AgentEventEmitter implementing IAgentEventEmitter
   with methods: EmitTextMessageStart, EmitTextMessageContent, EmitTextMessageEnd,
   EmitToolCallStart, EmitToolCallArgs, EmitToolCallEnd, EmitStateSnapshot, EmitStateDelta,
   EmitRunStarted, EmitRunFinished
2. Each method writes SSE-formatted events to the response stream
3. Event format: `data: {"type": "TEXT_MESSAGE_CONTENT", "content": "..."}\n\n`
4. Create SSE endpoint: POST /api/copilotkit that:
   - Sets Content-Type: text/event-stream
   - Accepts agent messages via query or POST body
   - Streams AG-UI events as agent runs
5. Add middleware that wraps agent tool calls to automatically emit TOOL_CALL events
6. Include token usage metadata in TOOL_CALL_END and RUN_FINISHED events
7. Test with curl: `curl -X POST http://localhost:5000/api/copilotkit -H "Content-Type: application/json" -d '{"messages":[{"role":"user","content":"hello"}]}'`
8. Commit: "feat: add AG-UI SSE endpoint"

Plan the event format first, then implement.
```

### Phase 0E — Frontend Scaffold

```
Read the spec at docs/dataverse-ai-forge-spec-v4.md:
- Section 10: Frontend Architecture (design tokens, typography, layout)
- Section 11: Frontend SSE Integration (provider setup, file structure)

Scaffold the Next.js frontend:

1. In frontend/, create Next.js 15 app with TypeScript + Tailwind CSS
2. Create lib/sse-types.ts (SSE event type definitions) and lib/sse-parser.ts (async SSE stream parser)
   lucide-react, geist (font)
3. Set up lib/themes.ts with dark + light token objects from spec section 10
4. Set up lib/agents.ts with the 10 agent definitions (name, icon, color)
5. Create ThemeProvider in components/layout/ using React context + CSS variables
6. In app/providers.tsx, wrap with <ForgeChatProvider sessionId={threadId}>
7. Create app/api/chat/route.ts as thin SSE proxy to backend /api/copilotkit
8. Create a minimal app/page.tsx with the 4-panel layout skeleton:
   - Sidebar (252px) | Workspace (flex) | Chat (348px)
   - All panels empty with bg colors from theme tokens
9. Add Geist and JetBrains Mono fonts
10. Verify: `npm run build && npm run dev` — see the dark layout
11. Commit: "feat: scaffold Next.js frontend with ForgeChatProvider + design tokens"

Plan first, then execute.
```

### Phase 0F — Chat Panel (Custom SSE)

```
Read the spec at docs/dataverse-ai-forge-spec-v4.md:
- Section 10: Chat Panel spec (message types, input, header)
- Section 11: Frontend SSE Integration (useForgeChat usage)

Build the custom chat panel using useForgeChat SSE integration:

1. Create components/chat/ChatPanel.tsx:
   - Use useForgeChatContext() to get messages, sendMessage, isStreaming
   - Header with Sparkles icon + "Assistant" title + agent badges + close button
   - Scrollable message list
   - Input with Paperclip icon + text field + gradient send button
2. Create components/chat/MessageRenderer.tsx:
   - Routes messages based on type metadata
   - Default: AgentBubble (left-aligned, agent badge + name, bg2 bubble)
   - User: right-aligned bubble with blueBg
3. Create components/chat/AgentBubble.tsx with agent identity badge
4. Create components/primitives/AgentDot.tsx (22px square with icon)
5. Create components/primitives/IconBtn.tsx (28px button)
6. Create components/primitives/Pill.tsx (inline badge)
7. Wire ChatPanel into the main layout page.tsx
8. Test: type a message, verify it appears in the UI (even if backend isn't connected)
9. Commit: "feat: add custom SSE chat panel with agent identity"

Plan the component tree first, then implement.
```

### Phase 1A — Business Analyst Agent

```
Read the spec:
- Section 2: Agent Roster (BA row)
- v3.1 addendum content now in section 4: Orchestration (BA flow)
- Section 7: find BA system prompt details

Implement the Business Analyst agent:

1. Create Agents/BusinessAnalystAgent.cs using GPT-4o
2. BA has NO tools — it's conversational only
3. System prompt should include:
   - 4 phases: LISTEN → ELICIT → STRUCTURE → CONFIRM
   - Output: structured Requirement Specification (SRS) in the format from the spec
   - Tone: professional but conversational, asks focused questions
4. Implement BA routing in the Solution Architect:
   - Architect evaluates if request is ambiguous/broad → handoff to BA
   - BA produces SRS → handoff back to Architect with requirements
5. Update handoff topology: BA → Architect only
6. Add SRS output as a custom message type in AG-UI events (STATE_DELTA with srs key)
7. Test: send "I need a feedback module" → should trigger BA questions
8. Commit: "feat: add Business Analyst agent with requirement elicitation"
```

### Phase 1B — Diagnostics Panel

```
Read the spec at docs/dataverse-ai-forge-spec-v4.md:
- Section 10: Diagnostics Panel (full visual spec)
- Section 11: SSE TOOL_CALL event capture

Build the diagnostics panel:

1. Create hooks/useDiagnostics.ts:
   - Derive DiagnosticEntry[] from toolCalls in ForgeChatContext
   - Store entries in state: { agent, toolName, timestamp, duration, payload, result, status }
   - Extract agent identity from tool name prefix (e.g., "dataModel.create_table")
2. Create components/diagnostics/DiagPanel.tsx:
   - 380px panel, toggled from chat header bug icon
   - Header: Bug icon + "Diagnostics" + connection status dot
   - Scrollable list of DiagEntry components
   - Auto-scroll to bottom on new entries
3. Create components/diagnostics/DiagEntry.tsx:
   - Agent badge + name + timestamp (mono)
   - Duration pill (green ≤ 1s, orange > 1s)
   - Terminal icon + tool name in monospace
   - Collapsible "Payload" section with JSON
   - Collapsible "Result" section with JSON
4. Create components/diagnostics/CodeBlock.tsx:
   - codeBg/codeText tokens, JetBrains Mono, max-height 180px, scroll, pre-wrap
5. Wire DiagPanel into main layout, toggle via showDiag state
6. Test with mock data first, then with real AG-UI events
7. Commit: "feat: add diagnostics panel with tool call capture"
```

### Phase 1C — Approval Cards (Human-in-the-Loop)

```
Read the spec:
- Section 4: Risk-Based Approval Model
- Section 10: Approval Card in chat message types
- Section 11: request_approval tool detection from SSE events

Implement approval flow:

1. Backend: create ApprovalMiddleware that classifies risk level (green/yellow/red)
   per the spec rules. Emit approval request as AG-UI STATE_DELTA.
2. Frontend: create hooks/useForgeApprovals.ts
   - useForgeApprovals() detecting request_approval tool call from SSE events
   - Renders ApprovalCard component
   - Blocks agent execution until user responds
3. Create components/chat/ApprovalCard.tsx:
   - Shows: operation type, target (table/field name), risk level pill
   - Reviewer score if available
   - Buttons: Approve (green), Edit (blue), Reject (red)
   - respond({ approved: true/false, edits: ... })
4. Test: trigger a create_table plan → should show approval card → approve → execute
5. Commit: "feat: add human-in-the-loop approval cards"
```

### Phase 1D — Sidebar + Solution Tree

```
Read the spec section 10: Sidebar Components.

Build the collapsible sidebar with solution tree:

1. Create components/layout/Sidebar.tsx:
   - 252px expanded, 52px collapsed (icon rail)
   - Logo + brand, search bar, nav items (Overview, Activity Log, ERD, Timeline)
   - Solution card with prefix + version
   - Collapsible tree: TABLES > table > fields, COMPONENTS, SECURITY
   - Environment status at bottom
   - Theme toggle + collapse button in footer
   - Token/cost pill (compact display from spec)
2. Wire useAgent() state to auto-update the tree when agents create tables
3. Collapsed state: icons only, tooltips on hover
4. Smooth transitions: width 0.2s ease
5. Commit: "feat: add collapsible sidebar with solution tree"
```

### Phase 1E — Thinking Trace + Shared State

```
Read the spec section 10: Agent Thinking / Reasoning Trace.

Implement reasoning transparency:

1. Backend: emit reasoning as STATE_DELTA with key "reasoning" before TEXT_MESSAGE events
2. Frontend: create components/chat/ThinkingTrace.tsx:
   - Collapsed by default: "▸ Reasoning" with agent color left accent
   - Expanded: dimmed text (t3), 11px, bg2/codeBg tint, 3px dashed left border
   - Appears ABOVE the agent's actual response
3. Update hooks/useForgeAgent.ts to capture reasoning from useAgent() state
4. Update MessageRenderer to insert ThinkingTrace before matching AgentBubble
5. Create hooks/useTokenTracker.ts:
   - Reducer that accumulates token counts from TOOL_CALL_END metadata
   - Exposes: totalTokens, totalCost, perAgentBreakdown
6. Wire token pill in sidebar footer
7. Commit: "feat: add thinking trace in chat + token cost tracker"
```

### Phase 2+ Prompts (Continue Pattern)

For phases 2 and 3, follow the same pattern. Each prompt:

```
Read the spec at docs/dataverse-ai-forge-spec-v4.md, section [X].

Implement [feature name]:

1. [Specific backend step]
2. [Specific frontend step]
3. [Integration step]
4. [Test step]
5. Commit: "feat: [description]"

Plan first, then execute.
```

Key Phase 2 tasks:
- Data Model Builder agent + plan/execute table/field tools
- Reviewer agent + validate_schema_design tool
- Tester agent + verify_table/field tools
- Handoff orchestration (5-agent loop: Architect → Builder → Reviewer → Approve → Execute → Tester → Documentor)
- Agent Timeline workspace view
- Token tracker expanded panel

Key Phase 3 tasks:
- UI Builder, Automation Builder, Security Builder agents
- Data Population, Documentor agents
- Full 10-agent orchestration
- Session persistence (SQLite)
- Light theme polish
- Timeline replay mode
- Demo script rehearsal

---

## 7. Workflow Tips

### Do

```
✅ Start every session with /clear
✅ Give Claude ONE focused task per session
✅ Always say "Plan first, then execute" or use Shift+Tab+Tab (Plan Mode)
✅ Read the plan before approving execution
✅ Run `dotnet build` and `npm run typecheck` after every change
✅ Commit after each working feature (Claude can do this)
✅ Use /compact when context gets heavy mid-session
✅ Reference the spec by section number: "see spec section 6.2"
✅ Use subagents for research: "use a subagent to investigate how Microsoft Agent Framework handles handoff orchestration"
✅ Update docs/phase-checklist.md after each session
```

### Don't

```
❌ Don't say "build the whole app" — always decompose
❌ Don't let Claude keep going if the build is broken — fix it immediately
❌ Don't put everything in root CLAUDE.md — use the hierarchy
❌ Don't duplicate spec content in CLAUDE.md — tell Claude where to find it
❌ Don't skip tests — they're your safety net when Claude refactors
❌ Don't stuff more than ~150 instructions in a single CLAUDE.md
❌ Don't use @file to embed the full spec — say "read docs/dataverse-ai-forge-spec-v4.md section X"
❌ Don't fight Claude on style — configure .editorconfig and Prettier instead
```

### Context Management

```
FRESH SESSION:  ~20k tokens baseline (CLAUDE.md + project structure)
AFTER 30 MIN:   ~80-120k tokens (code reads, edits, test output)
WARNING ZONE:   ~160k tokens → /compact or /clear and start new session
SWEET SPOT:     1 feature = 1 session = ~60-100k tokens
```

### Debugging with Claude

When something breaks:

```
"The build is failing with this error: [paste error].
Read the file at [path] and fix it. Don't change anything else.
Run dotnet build after fixing."
```

When an agent isn't behaving:

```
"The Solution Architect is not routing to the BA for ambiguous requests.
Read the spec section 4 (orchestration) and the architect's system prompt
in section 7.1. Compare with the current implementation and fix the routing logic."
```

### Progressive Spec Reading

Don't ask Claude to read the entire 2800-line spec. Point it to specific sections:

```
Good:  "Read spec section 6.2 (Data Model Builder tools) and implement plan_create_table"
Bad:   "Read the entire spec and implement the Data Model Builder"

Good:  "Read spec section 10, subsection 'Diagnostics Panel' and build it"
Bad:   "Build the frontend based on the spec"
```

### Using Visual Studio Alongside Claude Code

Claude Code runs in terminal. Visual Studio opens the .slnx. They coexist:

```
1. Claude Code makes changes in terminal
2. Visual Studio auto-reloads changed files (hot reload)
3. You review changes in VS Solution Explorer + diff view
4. Use VS for debugging (breakpoints, watch, immediate window)
5. Use Claude Code for implementation (faster, AI-assisted)
6. VS IntelliSense validates what Claude produced
```

For frontend, same pattern with VS Code:
```
1. Claude Code makes changes
2. VS Code + next dev auto-refreshes
3. You review in browser
4. Use VS Code for design tweaks if needed
```

---

## Quick Reference Card

| What | Command |
|------|---------|
| Scaffold new agent | `/new-agent DataModelBuilder` |
| Scaffold new tool | `/new-tool get_tables_list` |
| Full health check | `/check-health` |
| Read specific spec section | `"Read docs/dataverse-ai-forge-spec-v4.md section 6.2"` |
| Plan before coding | Start prompt with `"Plan first:"` or use Shift+Tab+Tab |
| Compact context | `/compact` |
| Start fresh | `/clear` |
| Commit work | `"Commit with message: feat: add X"` |
| Run backend | `cd backend && dotnet run --project src/DataverseForge.Api` |
| Run frontend | `cd frontend && npm run dev` |
| Run all tests | `cd backend && dotnet test DataverseForge.slnx` |

---

## Appendix: File-by-File Scaffold Reference

When you complete Phase 0A, you should have these files:

```
backend/
├── DataverseForge.slnx
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── src/
│   ├── DataverseForge.Domain/
│   │   ├── DataverseForge.Domain.csproj
│   │   ├── Interfaces/
│   │   │   └── IDataverseClient.cs
│   │   └── Models/
│   │       ├── EnvironmentInfo.cs
│   │       ├── SolutionInfo.cs
│   │       └── ProjectState.cs
│   ├── DataverseForge.Application/
│   │   ├── DataverseForge.Application.csproj
│   │   ├── Agents/           (empty, ready for Phase 0C)
│   │   └── Tools/            (empty, ready for Phase 0C)
│   ├── DataverseForge.Infrastructure/
│   │   ├── DataverseForge.Infrastructure.csproj
│   │   └── Dataverse/        (empty, ready for Phase 0B)
│   └── DataverseForge.Api/
│       ├── DataverseForge.Api.csproj
│       └── Program.cs        (minimal, health check only)
└── tests/
    ├── DataverseForge.Domain.Tests/
    ├── DataverseForge.Application.Tests/
    └── DataverseForge.Infrastructure.Tests/
```

After all Phase 0 sub-phases (A through F), you have a working:
- Backend that compiles, authenticates to Dataverse, has 1 agent, emits AG-UI SSE events
- Frontend that renders the dark layout, connects via custom SSE client, shows chat messages
- Docker compose to run both together