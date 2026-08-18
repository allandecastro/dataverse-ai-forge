# TODO

Open, tracked work only. Resolved items live in git history, not here. Last
synced 2026-08-18.

---

# Maintainability / debt

Still-open findings from the 2026-05-25 repo health-check.

- **Frontend test coverage.** The critical hooks are covered (`useForgeChat`,
  `useForgeApprovals`, `useTokenTracker`, `useAgent`, `useDiagnostics`,
  `useProjects`, `useAuditLog`, `useResizablePanel`), the SSE parse loop has the
  abort-signal check, and there are component tests (`EmptyChat`, `ApprovalCard`,
  `ConfirmDialog`). Remaining gap-fill is incremental: heavier components
  (`ChatPanel`, the workspace panels — `AgentTimeline` / `ActivityLog`).
- **`Tools/` test gap-fill (backend).** ~170 tool files; incremental, not a
  refactor. Pure validators/builders are the cheap wins (e.g.
  `FormStructureValidator` is now covered). Keep filling as tools are touched.
- **Agent prompts are large** (~3.8k LOC). Plan: (A) prompt-size budget test —
  *done* (`PromptSizeBudgetTests`); (B) extract dupes into `CommonPromptFragments`;
  (C) move stable reference tables to embedded `.md`. **Deferred on purpose** —
  editing prompts changes agent behaviour and needs a full retest per change
  (CLAUDE.md rule); low value vs. that risk.
- **Large `DataverseClient` partials** (`AppModules.cs` ~590 LOC, `Schema.cs`
  ~640 LOC — smaller than the old 1.3k estimate). Methods are interleaved by
  sub-concern, so a split means many scattered moves in core write-path code.
  **Recommend leaving as-is**; split opportunistically only when a file is next
  touched for a real change.

---

# Security

**Scope: a local, single-user desktop app** bound to `127.0.0.1`, never exposed
on a network. Under that threat model the security surface is essentially closed
— what's real is already in place:

- **CORS is origin-locked** to the app (`Cors:AllowedOrigins`, default
  `http://localhost:3000`), which blocks the only real desktop vector: a
  malicious website POSTing to the local backend fails its preflight.
  (`AllowAnyHeader/Method` is fine — the origin is the gate.)
- **`/api/agui` is rate-limited** (runaway-token-cost guard); `/api/settings/test`
  too.
- **Secrets are AES-GCM encrypted at rest**, and an **opt-in bearer-token** auth
  (`Auth:SharedSecret`) already exists if the backend is ever fronted.

N/A or not-security under this model (the former P1/P2 items):

- **HTTPS** — loopback traffic never leaves the machine; a cert only adds friction.
- **Request-size limit on `/api/agui`** — single user, self-DoS only.
- **"Tester verifies first" guard** — agent reliability, not security.
- **`localStorage` PII boundary** — the user's own machine; `clearSession()` +
  clear-wipes-DB already exist.

**Revisit all of the above ONLY if the model changes** (network exposure,
multi-user, shared/container host). Then: tighten CORS headers/methods,
auto-provision the bearer token from the desktop shell, enforce HTTPS, add
request-size limits, and move the encryption key into an OS keychain (macOS
Keychain / Windows DPAPI) instead of the on-disk `.secret.key`.

---

# Feature backlog

Non-security work agreed but not started. Keep short — ideas-without-decisions
live in chat, not here.

- **Desktop signing (open).** The packaged installers are UNSIGNED, so corporate
  Windows (SmartScreen / AppLocker / WDAC) can block the `.exe`. The fix is a
  code-signing certificate (an org cert wired into electron-builder, or Azure
  Trusted Signing) or an IT allowlist — not an Apple/MS developer account.
  macOS auto-update also needs signing (Squirrel.Mac). Deferred: no cert today.

_Shipped (see git history): in-app data folder, per-agent models, Electron owns
the .NET backend, desktop packaging (win + mac) published to GitHub Releases,
first-run onboarding, auto-update wiring, crisp icons._

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
