# Health-check execution log

Repo-wide health-check run on 2026-05-25. Findings + this turn's execution status.
Top observations and their disposition:

| # | Finding | Disposition |
|---|---------|-------------|
| 1 | `docs/dataverse-ai-forge-spec-v4.md` referenced by CLAUDE.md but missing | ✅ **Done** — restored from `c0d237e^` (2,859 lines). Deletion appears accidental (bundled with the 2026-05-13 backend-bugfix commit). User to confirm or re-delete. |
| 2 | 6–7 `Execute*` tools bypass `ApplyAndSaveAsync` (F-23 stale-state race) | ✅ **Done** — 7 tools converted to `ApplyAndSaveAsync`; bug pattern #8 added to CLAUDE.md; 852 tests still green. |
| 3 | ApprovalCard + ConfirmDialog WCAG hole (no aria-labels, no role=dialog) | ✅ **Done** — full a11y pass: `role="group"` + `aria-labelledby` on cards, `aria-expanded`/`aria-controls` on collapsibles, proper `role="tab(list\|panel)"` with roving tabindex, `role="dialog"` + `aria-modal` + focus management on ConfirmDialog (including focus restore). |
| 4 | Frontend test coverage ~5% (4 tests for 73 files) | 📋 Backlog — add to feature backlog below; gap-fill the 5 critical hooks first. |
| 5 | SSE: abort signal not checked in parse loop; defensive bubble in streamReducer | 📋 Backlog — low frequency but real; bundle with #4 when frontend testing pass happens. |
| 6 | Agent prompts at 3,806 LOC growing unbounded | 📋 Backlog — recommended 3-step plan: (A) add prompt-size budget test, (B) extract dupes into `CommonPromptFragments`, (C) move stable reference tables to embedded `.md`. Start with A. |
| 7 | `Tools/` has 170 files, ~58% test coverage | 📋 Backlog — incremental gap-fill; not a refactor. |
| 8 | `DataverseClient.AppModules.cs` 1,263 LOC, `Schema.cs` 946 LOC | 📋 Backlog — split by sub-concern when next touched. |
| 9 | Handoff tool inflation (8 specific + 1 generic) | 📋 Backlog — 15-min audit to decide consolidate vs. keep. |
| 10 | Bearer-token middleware on `/api/*` (P0 from security TODO below) | ✅ **Done** — backend gate (7 tests, opt-in via `Auth:SharedSecret`) + Next.js catch-all proxy at `/api/proxy/[...path]` that adds the bearer server-side so the secret never reaches the browser. P0 §1 fully closed. |
| 11 | CI `security.yml` uses `continue-on-error: true` | ✅ **Done** — flipped to blocking in commit 61a61e1 + Next.js bump (ddccddf) cleared the high-severity CVEs that would have blocked the next push. |
| 12 | `demo-data` type-imported in production `SidebarFooter` | ✅ **Done** — `DemoEnvironment` moved to `lib/types.ts` in commit 8a415f1. |
| — | `docs/dataverse-ai-forge-spec-v4.md` disposition | 📌 **User decision pending** — file was restored from `c0d237e^` (2,859 lines, dated 2026-05-13) because CLAUDE.md references it. The restore is in the PR; on merge, decide: (a) keep as-is, (b) refresh in a follow-up PR, or (c) re-delete and update CLAUDE.md to drop the reference. No urgent action — the spec is a historical reference, not a runtime contract. |

This turn also adds **bug pattern #8 (F-23 ApplyAndSaveAsync)** to CLAUDE.md so the
fix sticks for future agents.

---

# TODO — Security

Tracking security work deferred during the maintainability passes. The project today
is a **localhost-first developer tool** with no auth, no encryption-at-rest, and no
rate limiting. That is fine on `127.0.0.1` for solo dev, but every item below has to
be addressed before:

- exposing the backend on a network anyone else can reach,
- running it inside a multi-user container or shared dev box,
- packaging the app as something users install and run.

Items are ordered by blast radius, not effort.

---

## P0 — must fix before any non-localhost deployment

### ~~Unauthenticated API surface~~ — **done** (backend gate + Next.js catch-all proxy, 2026-05-25)

Was the P0 from the original audit. Now fully closed via two pieces of plumbing:

**Backend** — opt-in bearer-token middleware (lives in
[`BearerTokenAuthMiddleware.cs`](backend/src/DataverseForge.Infrastructure/Middleware/BearerTokenAuthMiddleware.cs),
covered by 7 integration tests in
[`BearerTokenAuthMiddlewareTests.cs`](backend/tests/DataverseForge.Infrastructure.Tests/Integration/BearerTokenAuthMiddlewareTests.cs)).
Set `Auth:SharedSecret` (via user-secrets or `DOTNET_Auth__SharedSecret`
env var, never `appsettings.json`) to require `Authorization: Bearer <secret>`
on every `/api/*` request. Leaving it unset is the no-op default — local dev
keeps working unchanged.

**Frontend** — Next.js catch-all proxy at
[`app/api/proxy/[...path]/route.ts`](frontend/src/app/api/proxy/%5B...path%5D/route.ts).
`lib/constants.BACKEND_URL` defaults to `/api/proxy`, so every browser-direct
call (`/api/sessions`, `/api/settings*`, `/api/projects/{id}/audit`, `/healthz`,
…) flows through the proxy — which adds the `FORGE_API_TOKEN` bearer header
server-side, so the secret never ships in the browser bundle. The dedicated
`/api/chat` route stays as-is (it has bespoke SSE + handshake-timeout wiring
this generic proxy doesn't duplicate).

**To enable end-to-end:** `dotnet user-secrets set "Auth:SharedSecret" "<value>"`
in `backend/src/DataverseForge.Api/`, set `FORGE_API_TOKEN=<same value>` in the
frontend env, restart both. Verify with `curl http://localhost:5000/api/sessions`
→ 401, and the UI continues to work because the browser hits `/api/proxy/...`
which forwards with the token.

### Plaintext secrets on disk
`user-settings.json` is written next to the binary with `AzureAI:ApiKey`,
`Dataverse:ClientSecret`, and `GitHub:Token` in cleartext.

**Fix (Windows-first):** wrap each secret with `IDataProtectionProvider` (DPAPI on
Windows, scoped data protection elsewhere) before persisting. Keep the file shape;
just encrypt the values.

**Files:** [UserSettingsService.cs](backend/src/DataverseForge.Infrastructure/Settings/UserSettingsService.cs).
Estimated effort: half a day including round-trip tests.

---

## P1 — important before any prod exposure

### CORS hardcoded to one origin
Even though [Program.cs:24-31](backend/src/DataverseForge.Api/Program.cs#L24) now reads
`Cors:AllowedOrigins` from config, `AllowAnyHeader().AllowAnyMethod().AllowCredentials()`
remains permissive. Combined with no auth, a malicious local page can drain the API.

**Fix:** restrict to a known list of headers/methods; require `[Authorize]`-equivalent
once auth lands.

### No rate limiting
A runaway agent loop or hostile request can burn Azure AI tokens or DDoS Dataverse
limits. ASP.NET Core has built-in rate limiting (`AddRateLimiter`).

**Fix:** add a fixed-window or token-bucket limiter on `/api/agui` (per-session or
per-IP, whichever fits the threat model).

### HTTPS not enforced
`Program.cs` doesn't call `UseHttpsRedirection()` or `UseHsts()`. Fine for HTTP
localhost dev; **a hard no for any deployment that touches the network**.

**Fix:** add the middleware behind an environment flag so dev-without-cert still
works.

### ~~SSE error path leaks `ex.Message`~~ — **done**
[ChatEndpoints.cs:124-143](backend/src/DataverseForge.Api/Endpoints/ChatEndpoints.cs#L124)
already logs the full exception server-side (correlatable via session id) and emits
a stable, generic message to the stream — `ex.Message` is never surfaced to the client.
This entry was stale; left here as a record that it's closed.

### No rate limit on `/api/settings/test` — **done** (2026-06-20)
The live-credential test endpoint was unthrottled (only `/api/agui` had a limiter),
usable as a credential-probe / timing oracle. Now behind a dedicated `settings-test`
fixed-window policy (default 10/min, `RateLimiting:SettingsTestPermitsPerMinute`).
See [Program.cs](backend/src/DataverseForge.Api/Program.cs) +
[SettingsEndpoints.cs](backend/src/DataverseForge.Api/Endpoints/SettingsEndpoints.cs).

---

## P2 — defense-in-depth / nice-to-have

### Request size limits on `/api/agui`
Big payloads (huge message histories, hostile uploads) can exhaust memory.
ASP.NET defaults are generous. Add a `[RequestSizeLimit]` or Kestrel limit.

### `localStorage` chat persistence has no PII boundary
Conversations may contain customer / Dataverse data. Add docs that say so, and
consider a "clear local data" flow surfaced in the UI (we have `clearSession()` —
make sure it nukes everything we care about).

### CI dependency check is non-blocking
[.github/workflows/security.yml](.github/workflows/security.yml) runs `npm audit
--audit-level=high` and `dotnet list package --vulnerable` with `continue-on-error:
true`. Bump to blocking before going to production.

### Code-enforced "Tester verifies first"
The Tester agent's prompt says "first action must be a `verify_*` tool call" but
nothing enforces this. A code guard (similar to [ChainLoopDecisions.cs:51](backend/src/DataverseForge.Application/Orchestration/ChainLoopDecisions.cs#L51)
forced-handoff for Tester) would make it bulletproof. Not
strictly security, but related — the prompt-only rule is what stops a Tester from
"verifying" by hallucinating instead of probing real state.

---

## Done elsewhere (don't re-add)

- ✓ Backend has `Nullable enable` + `TreatWarningsAsErrors` (compile-time sanity).
- ✓ No blocking `.Result` / `.Wait()` calls.
- ✓ HttpClient via factory (no new() in code).
- ✓ Structured logging (no `Log{Level}($"{interpolation}")`).
- ✓ No `dangerouslySetInnerHTML` / unsafe HTML in frontend; markdown sanitized.
- ✓ Settings inputs masked on the wire (server returns `••••` for secrets).
- ✓ Architecture test enforces Domain has zero deps.

---

# Feature backlog

Non-security work the team has agreed to do but hasn't started. Keep this list short
— ideas-without-decisions live in chat / a doc, not here.

## Persist tool calls + diagnostics across page reloads
Today `messages` are persisted in `localStorage` per session, but `toolCalls`,
`agentState`, and the diagnostic trail are NOT. After a page reload (or backend
restart) the Agent Timeline / Activity Log / Diagnostic panel start empty, even
though the chat still shows the user's history.

**Why it matters:** the user noted that Agent Timeline only showed the most recent
agent's tool calls — root cause for that *was* a reset bug (now fixed); but there's
still the broader issue that on a fresh page load, history is gone. For long sessions
that span multiple sittings, that hurts.

**Sketch:**
- New `STORAGE_TOOL_CALLS(sessionId)` key, mirror of `saveMessages` pattern.
- Cap to last N entries (~200) to keep the JSON blob small.
- Decide what to do with running tool calls on reload — most likely flag them as
  "incomplete" rather than persist `status:running` indefinitely.
- Re-hydrate `agentState.activeAgent` from the latest assistant message rather
  than from a cold default.

Small effort (1-2h) but design-touchy; revisit after the immediate UX fixes settle.

---

# Risk-evaluation rules (reference)

When a Plan tool returns a plan, it stamps a `riskLevel` that drives the approval
card's badge color and helps the agent decide whether to require approval. The
current rules — extracted from the actual Plan tools so they stay accurate:

| Level | Meaning | Examples |
|---|---|---|
| **red** | The plan is REJECTED — the operation will fail or is structurally invalid. The agent must NOT include it in any batch. | `PlanCreateField` for an exact-match existing field; `PlanCreateField localOptionSet` without `allowLocalOptionSet:true` opt-in; `PlanCreateForm`/`PlanCreateView` with a field that doesn't exist on the table. |
| **orange** | High-impact, hard-to-reverse change. Must require explicit user approval. | Creating tables, creating main forms, creating model-driven apps, deleting components, batch operations. |
| **yellow** | Medium-impact change. Approval recommended but the operation is recoverable. | Adding a field, adding an option to an option set, updating an option label, modifying field properties, alternate keys. |
| **green** | Low-impact / read-only-shaped operation. Approval optional. | Creating views (savedqueries), quickCreate / quickView forms, dashboards, populating reference data, generating documentation. |

**Where the rule is applied:** each `Plan*Tool` sets `RiskLevel` directly in its
returned record (search the `Tools/` folder for `RiskLevel:`). Frontend renders
the badge in `ApprovalCard` based on that string, no remapping.

**When to update:** if a new operation's blast radius doesn't match any tier above,
add it here AND update the corresponding Plan tool. Don't invent new tier names —
only the four above are recognized by the frontend approval card.
