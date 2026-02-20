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
- [x] `OrchestratingChatClient` — IChatClient wrapper resolving active agent per request
- [x] `AgUiSseWriter` — converts streaming responses to AG-UI SSE events
- [x] SSE endpoint: `POST /api/copilotkit` with CORS for localhost:3000
- [x] `AddAgUi()` DI registration

### Phase 0E — Frontend Scaffold
- [x] Next.js 16 + TypeScript + Tailwind CSS in `frontend/`
- [x] CopilotKit packages + Geist fonts + lucide-react
- [x] Theme tokens (dark + light) in `lib/themes.ts`
- [x] Agent definitions (10 agents) in `lib/agents.ts`
- [x] `ThemeProvider` with CSS custom properties
- [x] `CopilotKit` provider + AG-UI route proxy
- [x] 4-panel layout skeleton (Sidebar | Workspace | Chat | Diagnostics)

### Phase 0F — Chat Panel (Headless CopilotKit)
- [x] `ChatPanel` with `useCopilotChat()` headless hook
- [x] `MessageRenderer` routing user/assistant messages
- [x] `AgentBubble`, `AgentDot`, `IconBtn`, `Pill` primitives
- [x] Message input with gradient send button

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

### Phase 1C — Data Model Builder Agent (Backend)
- [x] Domain models: `TableCreationPlan`, `FieldCreationPlan`, `RelationshipCreationPlan`
- [x] `IDataverseClient` — 4 write methods: `CreateTableAsync`, `AddSolutionComponentAsync`, `CreateFieldAsync`, `CreateOneToManyRelationshipAsync`
- [x] `DataverseClient` — implementations with `OData-EntityId` header extraction
- [x] `PlanCreateTableTool` — pure utility, OData JSON generation, risk="orange"
- [x] `PlanCreateFieldTool` — pure utility, 17+ field types, lookup→warning, calculated/rollup→deferred
- [x] `PlanCreateOneToManyRelationshipTool` — pure utility, cascade config, ERD fragment generation
- [x] `ExecuteCreateTableTool` — CreateTable + AddSolutionComponent, state + OperationStack
- [x] `ExecuteCreateFieldTool` — CreateField, state + OperationStack
- [x] `ExecuteCreateOneToManyRelationshipTool` — CreateRelationship + AddSolutionComponent, state + OperationStack
- [x] `HandoffToDataModelBuilderTool` — SA → Builder handoff
- [x] `DataModelBuilderAgent` + `DataModelBuilderPrompt` — plan/execute execution pattern
- [x] `AddDataModelBuilder()` DI registration, SA handoff targets updated
- [x] 26 new unit tests (62 total across solution)

### Phase 1D — Diagnostics Panel (Frontend)
- [x] `useDiagnostics` hook with `useCopilotAction("*")` wildcard
- [x] `DiagPanel` (380px, toggled from chat header via Bug icon)
- [x] `DiagEntry` with agent badge, duration pill, collapsible payload/result
- [x] `CodeBlock` with monospace styling

### Phase 1E — Approval Cards (Human-in-the-Loop)
- [ ] Backend: `ApprovalMiddleware` with risk classification (green/yellow/red)
- [ ] Frontend: `useForgeApprovals` hook with `renderAndWaitForResponse`
- [ ] `ApprovalCard` component (Approve/Edit/Reject)

### Phase 1F — Sidebar + Solution Tree
- [ ] `Sidebar` (252px expanded, 52px collapsed)
- [ ] Solution tree: TABLES > fields, COMPONENTS, SECURITY
- [ ] Theme toggle, environment status, token/cost pill

### Phase 1G — Thinking Trace + Shared State
- [ ] Backend: reasoning as `STATE_DELTA`
- [ ] `ThinkingTrace` component (collapsed by default)
- [ ] `useTokenTracker` hook with per-agent breakdown

## Phase 2 — Builder & Review Agents

- [x] Data Model Builder agent + plan/execute table/field tools (moved to Phase 1C)
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
