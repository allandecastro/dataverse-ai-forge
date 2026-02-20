# Phase Checklist

Living progress tracker for Dataverse AI Forge implementation.

## Phase 0 — Foundation

### Phase 0A — Scaffold Backend Solution
- [x] Create `backend/` with `global.json`, `Directory.Build.props`, `Directory.Packages.props`
- [x] Create 4 projects: Domain, Application, Infrastructure, Api
- [x] Create 3 test projects: Domain.Tests, Application.Tests, Infrastructure.Tests
- [x] Create `DataverseForge.slnx` with solution folders
- [x] Add NuGet packages via central package management
- [x] Domain models: `IDataverseClient`, `EnvironmentInfo`, `SolutionInfo`, `ProjectState`
- [x] Minimal `Program.cs` with health check
- [x] `dotnet build` — 0 errors, 0 warnings

### Phase 0B — Dataverse Authentication
- [x] `DataverseClient` implementing `IDataverseClient` with MSAL auth
- [x] `DataverseAuthHandler` with `Azure.Identity` token acquisition
- [x] `IOptions<DataverseOptions>` configuration (EnvironmentUrl, TenantId, ClientId, ClientSecret)
- [x] `GetEnvironmentInfoAsync()` — WhoAmI + organizations OData
- [x] `GetSolutionsListAsync()` — unmanaged solutions query
- [x] `AddDataverseClient()` DI extension with `HttpClient` factory
- [x] Integration tests (skippable in CI)

### Phase 0C — First Agent (Solution Architect)
- [x] `SolutionArchitectAgent` using Microsoft Agent Framework + GPT-4o
- [x] `GetEnvironmentInfoTool` — wraps `IDataverseClient`
- [x] `GetSolutionsListTool` — wraps `IDataverseClient`
- [x] Azure OpenAI chat client registration (`AddAzureOpenAiChatClient()`)
- [x] Agent + tools registered in DI (`AddSolutionArchitect()`)
- [x] Unit tests with Moq

### Phase 0D — AG-UI SSE Endpoint
- [ ] `IAgentEventEmitter` with AG-UI event methods
- [ ] SSE endpoint: `GET /api/copilotkit/sse`
- [ ] Tool call event middleware
- [ ] Token usage metadata in events

### Phase 0E — Frontend Scaffold
- [ ] Next.js 15 + TypeScript + Tailwind CSS in `frontend/`
- [ ] CopilotKit packages + Geist/JetBrains Mono fonts
- [ ] Theme tokens (dark + light) in `lib/themes.ts`
- [ ] Agent definitions in `lib/agents.ts`
- [ ] `CopilotKit` provider + AG-UI route
- [ ] 4-panel layout skeleton (Sidebar | Workspace | Chat)

### Phase 0F — Chat Panel (Headless CopilotKit)
- [ ] `ChatPanel` with `useCopilotChat()` headless hook
- [ ] `MessageRenderer` routing by message type
- [ ] `AgentBubble`, `AgentDot`, `IconBtn`, `Pill` primitives
- [ ] Message input with send button

## Phase 1 — Core Agents & UI

### Phase 1A — Business Analyst Agent & Handoff Orchestration
- [x] `IAgent` interface with `Name`, `SystemPrompt`, `HandoffTargets`, `ProcessAsync`
- [x] `BusinessAnalystAgent` — conversational, no tools, LISTEN→ELICIT→STRUCTURE→CONFIRM
- [x] `HandoffToBusinessAnalystTool` — SA → BA handoff
- [x] `HandoffToSolutionArchitectTool` — BA → SA handoff
- [x] `AgentOrchestrator` — routes to active agent by `ProjectState.ActiveAgentName`
- [x] Handoff topology: SA ↔ BA
- [x] Unit tests for agents, tools, and orchestrator

### Phase 1B — Solution Architect Read-Only Tools Expansion
- [x] Domain models: `PublisherInfo`, `TableInfo`, `TableDetails` (with `ColumnInfo`, `RelationshipInfo`, `AlternateKeyInfo`)
- [x] Domain models: `SchemaValidationResult` (with `SchemaValidationIssue`), `OobTableRecommendation`
- [x] `IDataverseClient` — 3 new methods: `GetPublishersAsync`, `GetTablesListAsync`, `GetTableDetailsAsync`
- [x] `DataverseClient` — OData implementations with private DTOs
- [x] `GetPublishersTool` — non-readonly publishers
- [x] `GetTablesListTool` — filter by all/custom/oob/solution + searchTerm
- [x] `GetTableDetailsTool` — columns, relationships, alternate keys
- [x] `ValidateSchemaNameTool` — pure utility, regex validation, prefix prepend
- [x] `CheckOobTableCoverageTool` — pure utility, ~20 OOB table catalog, keyword matching
- [x] `GenerateErdMermaidTool` — reads from `ProjectState`, outputs Mermaid erDiagram
- [x] All 6 tools registered in `AddSolutionArchitect()`
- [x] 11 new unit tests (38 total across solution)

### Phase 1C — Diagnostics Panel (Frontend)
- [ ] `useDiagnostics` hook with `useCopilotAction("*")` wildcard
- [ ] `DiagPanel` (380px, toggled from chat header)
- [ ] `DiagEntry` with agent badge, duration pill, collapsible payload/result
- [ ] `CodeBlock` with monospace styling

### Phase 1D — Approval Cards (Human-in-the-Loop)
- [ ] Backend: `ApprovalMiddleware` with risk classification (green/yellow/red)
- [ ] Frontend: `useForgeApprovals` hook with `renderAndWaitForResponse`
- [ ] `ApprovalCard` component (Approve/Edit/Reject)

### Phase 1E — Sidebar + Solution Tree
- [ ] `Sidebar` (252px expanded, 52px collapsed)
- [ ] Solution tree: TABLES > fields, COMPONENTS, SECURITY
- [ ] Theme toggle, environment status, token/cost pill

### Phase 1F — Thinking Trace + Shared State
- [ ] Backend: reasoning as `STATE_DELTA`
- [ ] `ThinkingTrace` component (collapsed by default)
- [ ] `useTokenTracker` hook with per-agent breakdown

## Phase 2 — Builder & Review Agents

- [ ] Data Model Builder agent + plan/execute table/field tools
- [ ] Reviewer agent + `validate_schema_design` tool
- [ ] Tester agent + verify table/field tools
- [ ] 5-agent handoff loop: Architect → Builder → Reviewer → Approve → Execute → Tester → Documentor
- [ ] Agent Timeline workspace view
- [ ] Token tracker expanded panel

## Phase 3 — Full Agent Roster & Polish

- [ ] UI Builder, Automation Builder, Security Builder agents
- [ ] Data Population, Documentor agents
- [ ] Full 10-agent orchestration
- [ ] Session persistence (SQLite)
- [ ] Light theme polish
- [ ] Timeline replay mode
