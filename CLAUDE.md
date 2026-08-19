# Dataverse AI Forge

AI-powered platform that builds Microsoft Dataverse solutions through natural language conversation. Eight specialized agents collaborate (Solution Architect, Business Analyst, Data Model Builder, UI Builder, Data Population, Tester, Security Builder, Automation Builder); Documentor remains on the roadmap. Automation Builder v1 is lifecycle-management-only (rename / activate / deactivate / delete) for BPFs + Business Rules — creation deferred until XAML templates can be validated against a real Dataverse env.

## Architecture

- Backend: .NET 10 Clean Architecture in `/backend` (DataverseForge.slnx)
- Frontend: Next.js + Custom SSE Client (ForgeChatProvider) in `/frontend`
- Bridge: AG-UI protocol (SSE) between backend and frontend
- Agents: Microsoft Agent Framework with handoff orchestration
- LLM: Azure AI Foundry (any model — GPT-4o, Llama, Mistral, Phi, etc.)

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
- The code is the source of truth — there is no separate spec doc. Before changing an agent's behavior, read its prompt (`backend/src/DataverseForge.Application/Agents/Prompts/*.md`) and the relevant tool
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

### 8. `ApplyAndSaveAsync` vs raw `SaveAsync` (F-23 stale-state race)

**Symptom**: a tool reads `state` at the top of the method, awaits 5–30s of
Dataverse work, mutates `state`, then `SaveAsync(state)` — silently clobbering
any concurrent write that landed in the window (parallel agent action, SSE
handoff message, audit emitter). Single-threaded sessions hide this, but
parallel `get_app_full` + `handoff_to_tester` is a real pattern in this
codebase, so the race triggers in production.

**Fix**: any `Execute*` / `Delete*` tool that mutates `ProjectState` after a
Dataverse round-trip must use the
[`ApplyAndSaveAsync`](backend/src/DataverseForge.Application/Contracts/ProjectStateStoreExtensions.cs)
extension. The lambda re-reads fresh state inside it; capture artifact ids,
audit details, and `planId` as locals BEFORE the call and only reference
those locals from inside the lambda (no `state.X` captures).

Reference implementation: [`ExecuteCreateTableTool.cs:58-89`](backend/src/DataverseForge.Application/Tools/ExecuteCreateTableTool.cs).
Both the success path AND the catch block must use `ApplyAndSaveAsync` — a
mixed tool (one path raw, one path safe) still races. Side-effect-only work
that does NOT touch `ProjectState` (e.g. `PublishAppAsync`, best-effort
`AddSolutionComponentAsync`) stays outside the lambda; only state mutations
go inside.

While you're there: lookups should use `state.LookupPlan(planId, out var payload)`
(returns `PlanLookupStatus.AlreadyExecuted` / `NotFound` / found) instead of raw
`state.PendingPlans.TryGetValue(...)` so the `ALREADY_EXECUTED` early-return is
consistent across tools.

### 9. Autonumber lives on the primary field — never create a new column for it

**Symptom**: user asks for an "auto-generated ID / reference number" and the agent
calls `plan_create_field(fieldType: "autonumber")`, adding a SECOND string column
alongside the table's primary name attribute — or worse, tries to build the
autonumber ON the primary by re-creating it, which fails (the primary already
exists).

**Fix**: a table's primary name attribute IS where an autonumber format belongs.
`plan_create_field` rejects any attempt whose `fieldLogicalName` equals
`details.PrimaryNameAttribute` (planId `rejected_primary_field_`, `PlanRisk.Red`)
and routes the agent to `plan_modify_field` with `autoNumberFormat` instead;
`plan_modify_field` allows `autoNumberFormat` on String attributes only. Always
resolve `PrimaryNameAttribute` via `GetTableDetailsAsync` first — do not trust the
agent to know which field is primary. Reference: `PlanCreateFieldTool.cs`,
`ExecuteCreateFieldTool.cs` (skip on `rejected_primary_field_`), `PlanModifyFieldTool.cs`.

### 10. The data-dir move marker is GLOBAL — test/CLI processes must not apply it

**Symptom**: a `dotnet test` run (or any second process) boots, `ForgePaths`
resolves the data dir, `ExecutePendingMove` runs the user's queued "Change
location" move, and the real credentials (`user-settings.json`, `.secret.key`,
`state.db`) get relocated behind the user's back to whatever folder they were
picker-testing.

**Fix**: the pending-move marker + pointer live in the OS config dir (one per user,
shared across every process), so any process that resolves paths can trigger the
move. `ExecutePendingMove` early-returns when `IsPinnedByEnv` (the `DVFORGE_DATA_DIR`
env var is set). Test hosts MUST pin `DVFORGE_DATA_DIR` to a temp dir (see the test
`ModuleInitializer`) and `InMemoryWebAppFactory` forces the `AzureAI` config empty
so a stray pointer can't make a live LLM call. Never let a non-user process resolve
`ForgePaths` without an env pin.

## Progress

The code is the source of truth for current behavior; agent capabilities are
described in this file's header and in each agent's prompt under
`backend/src/DataverseForge.Application/Agents/Prompts/`. Open work is tracked in
GitHub Issues, not a checked-in backlog file.