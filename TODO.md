# TODO

Open, tracked work only. Resolved items live in git history, not here. Last
pruned 2026-08-17 (dropped everything shipped since the 2026-05-25 health-check:
API auth, encrypted-secrets-at-rest, durable SQLite stores, tool-call restore,
SSE error-leak fix, `/api/settings/test` rate limit, blocking CI security scan).

---

# Maintainability / debt

Still-open findings from the 2026-05-25 repo health-check.

- **Frontend test coverage is thin.** The five critical hooks are now covered
  (`useForgeChat`, `useForgeApprovals`, `useTokenTracker`, `useAgent`,
  `useDiagnostics`), and the SSE parse loop already has the abort-signal check.
  Remaining gap-fill: component-level tests (ChatPanel, workspace panels) and the
  smaller utils/hooks (`useProjects`, `useAuditLog`, `useResizablePanel`).
- **Agent prompts are large** (~3.8k LOC and growing). Plan: (A) prompt-size
  budget test — *done* (`PromptSizeBudgetTests`); (B) extract duplicated blocks
  into `CommonPromptFragments`; (C) move stable reference tables to embedded
  `.md`. Continue with B then C.
- **Large files to split by sub-concern when next touched:**
  `DataverseClient.AppModules.cs` (~1.3k LOC), `DataverseClient.Schema.cs`
  (~950 LOC). Not a big-bang refactor; split incrementally.
- **`Tools/` (~170 files, ~58% covered):** incremental test gap-fill, not a
  refactor.

---

# Security

The app is a **localhost-first developer tool** today. Every item below has to
be addressed before exposing the backend on a reachable network, running it in a
multi-user/container box, or shipping it as an installed multi-user app. Ordered
by blast radius, not effort. (Reviewed together before starting — some may need
re-verifying against current code.)

## P1 — before any non-localhost exposure

- **CORS is permissive.** [Program.cs](backend/src/DataverseForge.Api/Program.cs)
  reads `Cors:AllowedOrigins` from config but still
  `AllowAnyHeader().AllowAnyMethod().AllowCredentials()`. Restrict to a known set
  of headers/methods; require auth once it lands.
- **Rate limiting coverage.** Confirm every expensive endpoint is limited
  (`/api/agui`, plus the already-limited `/api/settings/test`). A runaway agent
  loop can burn Azure AI tokens or hit Dataverse limits.
- **HTTPS not enforced.** No `UseHttpsRedirection()` / `UseHsts()`. Add behind an
  env flag so cert-less localhost dev still works.

## P2 — defense-in-depth

- **Request size limits on `/api/agui`** (`RequestSizeLimit` / Kestrel) — big
  histories / hostile payloads can exhaust memory.
- **Code-enforced "Tester verifies first."** The Tester prompt says its first
  action must be a `verify_*` call, but nothing enforces it. Add a guard like
  [ChainLoopDecisions](backend/src/DataverseForge.Application/Orchestration/ChainLoopDecisions.cs)'
  forced-handoff.
- **`localStorage` PII boundary.** Conversations can hold Dataverse/customer
  data. `clearSession()` + clear-wipes-DB exist; document the boundary and keep
  the clear flow exhaustive.

---

# Feature backlog

Non-security work agreed but not started. Keep short — ideas-without-decisions
live in chat, not here.

- **Desktop packaging (follow-up).** Electron now owns the .NET backend in dev;
  the packaged build still needs the published backend binary bundled per
  platform under `resources/backend` (electron-builder config + publish step).
- _(add new items here)_

---

# Risk-evaluation rules (reference)

When a Plan tool returns a plan it stamps a `riskLevel` that drives the approval
card's badge color and whether the agent requires approval. Extracted from the
Plan tools so it stays accurate:

| Level | Meaning | Examples |
|---|---|---|
| **red** | Plan is REJECTED — the op will fail or is structurally invalid. Must NOT be batched. | `PlanCreateField` for an exact-match existing field; local option set without `allowLocalOptionSet:true`; form/view referencing a field that doesn't exist. |
| **orange** | High-impact, hard-to-reverse. Requires explicit approval. | Creating tables, main forms, model-driven apps; deleting components; batch operations. |
| **yellow** | Medium-impact, recoverable. Approval recommended. | Adding a field, adding/updating an option-set option, modifying field properties, alternate keys. |
| **green** | Low-impact / read-only-shaped. Approval optional. | Views (savedqueries), quickCreate/quickView forms, dashboards, populating reference data, docs. |

**Where applied:** each `Plan*Tool` sets `RiskLevel` in its returned record
(search `Tools/` for `RiskLevel:`); the frontend `ApprovalCard` renders the badge
from that string, no remapping. Only these four tiers are recognized — don't
invent new names.
