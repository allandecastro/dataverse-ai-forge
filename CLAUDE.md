# Dataverse AI Forge

AI-powered platform that builds Microsoft Dataverse solutions through natural language conversation. Seven specialized agents collaborate (Solution Architect, Business Analyst, Data Model Builder, UI Builder, Data Population, Tester, Security Builder); two more (Automation, Documentor) are scoped on the roadmap as not-yet-built.

## Architecture

- Backend: .NET 10 Clean Architecture in `/backend` (DataverseForge.slnx)
- Frontend: Next.js + Custom SSE Client (ForgeChatProvider) in `/frontend`
- Bridge: AG-UI protocol (SSE) between backend and frontend
- Agents: Microsoft Agent Framework with handoff orchestration
- LLM: Azure AI Foundry (any model — GPT-4o, Llama, Mistral, Phi, etc.)
- Spec: Full specification in `/docs/dataverse-ai-forge-spec-v4.md`

## Build & Test

```bash
# Backend
cd backend && dotnet build DataverseForge.slnx && dotnet test DataverseForge.slnx
cd backend && dotnet run --project src/DataverseForge.Api

# Frontend
cd frontend && npm install && npm run build && npm run lint
cd frontend && npm run dev
```

## Key Conventions

- See [UNSUPPORTED.md](UNSUPPORTED.md) for the list of capabilities intentionally out of scope (calculated/formula columns, plugins, business rules, BPFs, custom APIs, field-level / column security profiles, documentation generation). Security roles (CRUD) shipped in Phase 6; model-driven apps + dashboards + charts shipped in Phases 1-5. When a user asks for an unsupported capability, agents reject with a maker-portal pointer instead of half-building it.
- Always read the spec in `/docs/dataverse-ai-forge-spec-v4.md` before implementing a feature
- Backend follows Clean Architecture: Domain has zero dependencies, Application references Domain only, Infrastructure implements interfaces
- Every agent tool follows the plan_*/execute_* split pattern
- Every write operation requires an entry in the operation stack for undo support
- Use central package management (Directory.Packages.props) — never pin versions in individual .csproj
- Commit after each working unit with conventional commits: `feat:`, `fix:`, `refactor:`, `docs:`

## RECURRING BUG PATTERNS — read before adding/changing tools

Bugs we have hit multiple times. If a new tool fits one of these shapes, apply the
fix proactively instead of waiting for it to fail in user testing.

### 1. Agent will mangle any OData/XML JSON you ask it to pass

**Symptom**: agent passes a literal placeholder like `"{...payload for X...}"` or a
truncated/quoted version of a multi-KB JSON blob → Dataverse returns 400 *"Stream
was not readable"* / *"unreadable payload"* / similar.

**Affected tools** (all `Execute*` that take JSON payloads as a parameter):
PlanCreateTable, PlanCreateField, PlanCreateGlobalOptionSet, PlanCreateOneToManyRelationship,
PlanCreateManyToManyRelationship, PlanCreateForm, PlanCreateView, PlanCreateDashboard,
PlanCreateBpf, PlanCreateBusinessRule, PlanCreateCustomApi, PlanCreateAlternateKey,
PlanCreateModelDrivenApp, PlanModifyField, PlanCreateSecurityRole, PlanCreateFieldSecurityProfile.

**Fix — planId-by-reference pattern**:
1. `Plan*Tool` generates a `planId`, calls `state.AddPendingPlan(planId, payloadJson)`,
   `await projectStateStore.SaveAsync(...)`, returns the planId on the plan record.
2. `Execute*Tool` accepts the `planId` (NOT the JSON), looks up
   `state.PendingPlans[planId]`, sends to Dataverse, then moves to `state.ExecutedPlans`
   and removes from `PendingPlans`.
3. Both tools take `IProjectStateStore` + `ISessionContext` in their constructors.
4. The `[Description]` on Execute's `webApiPayloadJson` parameter must say
   "planId from plan_X — payload stored server-side, do NOT construct it yourself".

Reference implementation: `PlanCreateFieldTool` + `ExecuteCreateFieldTool`.

### 2. Dataverse 4xx/5xx without a useful body

**Symptom**: `EnsureSuccessStatusCode()` throws `"Response status code does not
indicate success: 400 (Bad Request)."` with no detail. Agent invents reasons.

**Fix**: every write path in `DataverseClient.*.cs` uses `EnsureSuccessOrSurfaceAsync`
(in `DataverseClient.cs`) which reads the response body and parses
`error.message` from the OData envelope. Never use raw `EnsureSuccessStatusCode()`
on a POST/PUT/PATCH/DELETE.

### 3. Mixed-case schema names

Dataverse logical names are lowercase. Agents pass `eppc_Type`, `eppc_GiftName`, etc.
Plan tools must lowercase the `Name` / logical name automatically and surface a
warning. Do NOT trust that the agent will respect case rules from the prompt.

### 4. Polymorphic OData types need `@odata.type` on every element

When a Dataverse entity has a collection of a polymorphic complex type (e.g.
`OptionSetMetadata.Options` is `Collection(OptionMetadata)` and `OptionMetadata`
has derived types `StateOptionMetadata`/`StatusOptionMetadata`), each element in
the array MUST carry its own `@odata.type` discriminator. Same for `Label` and
`LocalizedLabel` inside Display/Description fields.

Source: <https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/use-web-api-metadata>

### 5. Don't claim a fix landed without verifying end-to-end

**Don't**: write code, run `dotnet test`, say "fixed". Tests passing ≠ user flow works.
The full chain involves prompt → SDK arg deserialization → tool logic → Dataverse →
SSE → frontend rendering, and unit tests cover at most one link.

**Do**: after a fix that affects an agent flow, ask the user to retest the specific
flow (table creation, option set, etc.) before moving on. If the same bug class
shows up again, treat that as evidence the fix didn't work — don't pile on more
"fixes" until the original is validated.

### 6. Restart-required changes

Changes to **any backend file** require the user to restart `DataverseForge.Api`.
Changes to .csproj/.props require a full clean+build. The dev loop is:
1. Stop the backend (Ctrl+C in its terminal — file locks block builds otherwise).
2. `dotnet build DataverseForge.slnx`.
3. `dotnet run --project src/DataverseForge.Api`.
4. Hard-refresh the frontend (Ctrl+Shift+R) to pick up new SSE event shapes.

If a build fails with `MSB3027 ... locked by DataverseForge.Api`, the user has the
process still running — do not try to kill it. Ask them to stop it first.

### 7. Routing — agents pick wrong tool on first try

When the user asks something ambiguous, agents tend to invent a logical name and
call `get_table_details(tableLogicalName: "guessed_name")` rather than the right
list/search tool. Concrete phrasing → tool mappings live in
[SolutionArchitectPrompt.cs](backend/src/DataverseForge.Application/Agents/SolutionArchitectPrompt.cs)
under `"LIST X" QUESTIONS`. Add to that table when you spot a new bad-routing case.

## Progress

See `/docs/dataverse-ai-forge-spec-v4.md` for the full specification.