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
- [x] Backend: `DataModelBuilderPrompt` updated with APPROVAL GATE section + risk classification
- [x] Frontend: `useForgeApprovals` hook with `useCopilotAction` + `renderAndWaitForResponse`
- [x] `ApprovalCard` component (Approve/Edit/Reject) with risk-colored border + collapsible details
- [x] Types: `PendingApproval`, `ApprovalStatus`, `RiskLevel` in `lib/types.ts`
- [x] `ChatPanel` updated to show `ActionExecutionMessage` for `request_approval`
- [x] `Pill` extended with "yellow" color for medium-risk operations

### Phase 1F — Sidebar + Solution Tree
- [x] `Sidebar` (252px expanded, 52px collapsed)
- [x] Solution tree: TABLES > fields, COMPONENTS, SECURITY
- [x] Theme toggle, environment status, token/cost pill

### Phase 1G — Thinking Trace + Shared State
- [x] Backend: `AgUiSseWriter` STATE_DELTA emission (activeAgent + reasoning), `<think>` tag buffering
- [x] Backend: `OrchestratingChatClient` yields `forge_agent_name` metadata, `Program.cs` passes agent name
- [x] Backend: Agent prompts (SA, BA, DMB) updated with REASONING TRACE section
- [x] Backend: 4 new unit tests for STATE_DELTA, think parsing, active agent, usage metadata
- [x] Frontend: `ThinkingTrace` component (collapsed by default, agent-colored dashed border)
- [x] Frontend: `MessageRenderer` parses `<think>` tags, renders ThinkingTrace above AgentBubble
- [x] Frontend: `useTokenTracker` hook with reducer-based per-agent accumulation
- [x] Frontend: `pricing.ts` with model rates + cost calculator for all 10 agents
- [x] Frontend: Sidebar accepts dynamic `tokenCount`/`cost` props wired from `page.tsx`

## Phase 2 — Governance Agents & Workspace Views

### Phase 2A — Reviewer Agent (Pre-Execution Validation)
- [x] `ReviewerAgent` + `ReviewerPrompt` (GPT-4o-mini, read-only)
- [x] `ValidateSchemaDesignTool` — naming, relationships, performance, security, limits, best practices; returns score 0-100 + issues[]
- [x] `HandoffToReviewerTool` (SA → Reviewer) + `HandoffToSolutionArchitectTool` (Reviewer → SA)
- [x] DI registration (`AddReviewer()`) + orchestrator resolver updated
- [x] Unit tests for Reviewer agent + validate tool
- [x] SA prompt updated with Reviewer in team list + handoff rules

### Phase 2B — Tester Agent (Post-Execution Verification)
- [x] `TesterAgent` + `TesterPrompt` (GPT-4o-mini, read-only)
- [x] `VerifyTableCreationTool` — re-reads table from API, compares expected vs actual, returns PASS/FAIL/PARTIAL
- [x] `VerifyFieldCreationTool` — verifies type, constraints, searchable, required
- [x] `VerifyRelationshipCreationTool` — verifies cascade behavior, lookup field
- [x] `RunFullValidationTool` — reads all components from ProjectState, runs all verifications
- [x] `HandoffToTesterTool` (SA/DMB → Tester)
- [x] `IDataverseClient` — add `GetTableMetadataAsync`, `GetFieldMetadataAsync`, `GetRelationshipMetadataAsync` for re-reading
- [x] DI registration (`AddTester()`) + orchestrator resolver updated
- [x] Unit tests for all verify tools

### Phase 2C — Documentor Agent
- [x] `DocumentorAgent` + `DocumentorPrompt` (GPT-4o-mini)
- [x] `GenerateDataDictionaryTool` — reads ProjectState tables/fields, outputs markdown
- [x] `GenerateChangelogEntryTool` — reads OperationStack, outputs changelog markdown
- [x] `GenerateErdDocTool` — Mermaid ERD from ProjectState (wraps existing ERD tool)
- [x] `PushDocumentationToGitHubTool` — uses Octokit to commit docs to repo
- [x] `HandoffToDocumentorTool` (SA → Documentor)
- [x] DI registration (`AddDocumentor()`)
- [x] Unit tests

### Phase 2D — Full Handoff Loop (5-Agent Schema Cycle)
- [x] End-to-end flow: SA → DMB(plan) → Reviewer → User Approval → DMB(execute) → Tester → Documentor → SA
- [x] DMB prompt updated with handoff targets (Reviewer, Tester, SA)
- [x] SA prompt updated: ALWAYS hand off to Reviewer before presenting, ALWAYS Tester after execution, ALWAYS Documentor after test
- [x] `useAgent()` hook — shared state sync from STATE_DELTA (currentAgent, phase, tables)
- [x] `useCopilotReadable()` — send workspace context to agents
- [x] MessageRenderer: dynamic agent name from STATE_DELTA (replace hardcoded "SolutionArchitect")
- [x] Sidebar tree: live refresh from agent state (replace demo data)
- [x] Integration test: full cycle with mocked Dataverse

### Phase 2E — Workspace Views (Dashboard + ERD + Table Detail)
- [ ] `Dashboard` workspace view — stat cards (tables, fields, relationships count) + component grid + recent activity
- [ ] `ErdDiagram` workspace view — Mermaid renderer on dot-grid background
- [ ] `TableDetail` workspace view — field table with type/required/searchable pills, relationships
- [ ] `ActivityLog` workspace view — full-width table (timestamp, agent, tool, duration, status)
- [ ] Workspace tab navigation (Overview / ERD / Activity)
- [ ] Sidebar nav items wired to workspace views

### Phase 2F — Agent Timeline + Token Panel
- [ ] `AgentTimeline` workspace view — horizontal Gantt bars per agent, color-coded
- [ ] Timeline: hover tooltip (tool name, duration, status), click → jump to diagnostics entry
- [ ] Timeline: total elapsed time + session cost in top-right
- [ ] `TokenPanel` — expanded breakdown table: Agent | Model | Input | Output | Cost
- [ ] Sidebar token pill click → opens TokenPanel overlay
- [ ] `useTokenTracker` wired to real AG-UI `RUN_FINISHED` metadata

### Phase 2G — Data Model Builder Tools Expansion
- [ ] `PlanCreateManyToManyRelationshipTool` + `ExecuteCreateManyToManyRelationshipTool`
- [ ] `PlanCreateGlobalOptionSetTool` + `ExecuteCreateGlobalOptionSetTool`
- [ ] `PlanAddOptionToOptionSetTool` + `ExecuteAddOptionToOptionSetTool`
- [ ] `PlanCreateAlternateKeyTool` + `ExecuteCreateAlternateKeyTool`
- [ ] `PlanModifyFieldTool` + `ExecuteModifyFieldTool`
- [ ] `IDataverseClient` — corresponding write methods
- [ ] Unit tests for all new tools

## Phase 3 — Full Agent Roster & Polish

### Phase 3A — UI Builder Agent
- [ ] `UIBuilderAgent` + `UIBuilderPrompt` (GPT-4o-mini)
- [ ] `GetFormsListTool`, `GetFormDefinitionTool` — read-only
- [ ] `PlanCreateFormTool` + `ExecuteCreateFormTool` — main + quickCreate forms
- [ ] `PlanCreateViewTool` + `ExecuteCreateViewTool` — system views with FetchXML
- [ ] `PlanCreateDashboardTool` + `ExecuteCreateDashboardTool`
- [ ] `PlanCreateModelDrivenAppTool` + `ExecuteCreateModelDrivenAppTool`
- [ ] `HandoffToUIBuilderTool` (SA → UIBuilder)
- [ ] `IDataverseClient` — form/view/dashboard CRUD methods
- [ ] Tester: `VerifyFormCreationTool`, `VerifyViewCreationTool`
- [ ] Unit tests

### Phase 3B — Automation Builder Agent
- [ ] `AutomationBuilderAgent` + `AutomationBuilderPrompt` (GPT-4o)
- [ ] `PlanCreatePluginTool` — generates C# code, registration steps, unit tests
- [ ] `PlanCreatePluginProjectTool` — project structure, .csproj, build instructions
- [ ] `ExecutePushToGitHubTool` — Octokit branch + commit + PR
- [ ] `PlanCreateBpfTool` + `ExecuteCreateBpfTool` — stages, steps, cross-entity
- [ ] `PlanCreateBusinessRuleTool` + `ExecuteCreateBusinessRuleTool`
- [ ] `PlanCreateCustomApiTool` + `ExecuteCreateCustomApiTool`
- [ ] `HandoffToAutomationBuilderTool` (SA → AutomationBuilder)
- [ ] Reviewer: `ValidatePluginCodeTool`
- [ ] Tester: `VerifyBpfCreationTool`
- [ ] Unit tests
- [ ] Frontend: code preview in approval cards for plugin review

### Phase 3C — Security Builder Agent
- [ ] `SecurityBuilderAgent` + `SecurityBuilderPrompt` (GPT-4o-mini)
- [ ] `GetSecurityRolesTool`, `GetSecurityRoleDetailsTool` — read-only
- [ ] `PlanCreateSecurityRoleTool` + `ExecuteCreateSecurityRoleTool` — privilege matrix
- [ ] `PlanCreateFieldSecurityProfileTool` + `ExecuteCreateFieldSecurityProfileTool`
- [ ] `AnalyzeSecurityGapsTool` — PII detection, over-privileged roles, missing coverage
- [ ] `HandoffToSecurityBuilderTool` (SA → SecurityBuilder)
- [ ] Tester: `VerifySecurityRoleTool`
- [ ] `IDataverseClient` — security role CRUD methods
- [ ] Unit tests

### Phase 3D — Data Population Agent
- [ ] `DataPopulationAgent` + `DataPopulationPrompt` (GPT-4o-mini)
- [ ] `PlanPopulateReferenceDataTool` + `ExecutePopulateReferenceDataTool` — seed data via $batch
- [ ] `PlanGenerateReferenceDataTool` — AI suggests appropriate seed values
- [ ] `PlanGenerateTestDataTool` + `ExecuteGenerateTestDataTool` — realistic test records
- [ ] `CountRecordsTool` — read-only record count
- [ ] `HandoffToDataPopulationTool` (SA → DataPopulation)
- [ ] `IDataverseClient` — batch record creation/deletion methods
- [ ] Unit tests

### Phase 3E — Full 10-Agent Orchestration
- [ ] All 10 agents registered in orchestrator resolver
- [ ] SA prompt updated with full 9-agent team + handoff rules
- [ ] Full handoff topology enforced (no builder→builder, governance always returns to SA)
- [ ] Undo support via OperationStack reversal
- [ ] Error recovery: partial failure handling, retry logic
- [ ] Integration test: full "feedback module" scenario with all agents

### Phase 3F — Session Persistence + Polish
- [ ] SQLite session persistence (save/restore ProjectState + conversation history)
- [ ] Project selector UI (list previous sessions, resume, create new)
- [ ] Light theme polish (verify all components)
- [ ] Agent activity animations (streaming dots, phase transitions)
- [ ] Timeline replay mode (playhead scrubber)
- [ ] Token tracker: "manual equivalent" comparison line ($0.47 vs $4,800)
- [ ] Diagnostics panel: filter by agent, search tool names
- [ ] BPF stage visualizer component
- [ ] SRS card renderer (BA requirement specs in chat)
- [ ] OpenTelemetry + Aspire dashboard integration

### Phase 3G — Demo Readiness
- [ ] Demo script rehearsal with all panel states
- [ ] Fallback recordings for live demo safety
- [ ] README + open source prep
- [ ] docker-compose verified end-to-end
