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

### Unauthenticated API surface
Every endpoint accepts anonymous requests today. Anyone with network access to the
backend port can:

- read sessions: `GET /api/sessions`, `GET /api/projects/{id}/audit`
- **rewrite Azure AI / Dataverse / GitHub credentials**: `PUT /api/settings`
- delete projects: `DELETE /api/sessions/{id}`
- drain Azure AI tokens via `/api/agui`

**Fix:** add a single bearer-token middleware in front of all `/api/*` endpoints. A
shared secret read from configuration is sufficient for the current threat model;
move to OAuth/AAD when multi-user is on the table.

**Files:** [Program.cs](backend/src/DataverseForge.Api/Program.cs), all
`Endpoints/*.cs`. Estimated effort: half a day including tests.

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

### SSE error path leaks `ex.Message`
[ChatEndpoints.cs](backend/src/DataverseForge.Api/Endpoints/ChatEndpoints.cs#L116)
writes `Agent error: {ex.Message}` directly to the stream. Some frameworks include
file paths or connection strings in messages.

**Fix:** log the full exception server-side; emit a stable opaque message
(`"Agent error — see server logs"`) plus a correlation id the user can quote.

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
