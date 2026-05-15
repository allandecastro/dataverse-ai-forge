# Smoke Re-run Pass 2 — 2026-05-13 (afternoon)

Tight 4-test checklist (~15 min) to close out the re-run from this morning.
After commit `6b07431` ships two more fixes (N1 + N2), there are four things
left to verify before we can call the smoke green:

- **G1** — count_records actually works on SA now (was: "at least 1 record" theatre)
- **G2** — SecurityBuilder surfaces the real Dataverse error when get_privileges_for_table fails (was: generic "Function failed.")
- **G3** — AutomationBuilder full lifecycle on a real workflow (this was F7 — never exercised)
- **G4** — SecurityBuilder delete safety pre-flight (this was F8 — was blocked on G2)

## Before you start

Stop + rebuild + restart the backend so the new prompts + tools take effect:

```powershell
# In the backend window (Ctrl+C if it's running)
cd C:\Users\allan.de.castro\Documents\GitHub\dataverse-ai-forge\backend
dotnet build DataverseForge.slnx
dotnet run --project src/DataverseForge.Api
```

Hard-refresh the frontend (Ctrl+Shift+R) once the backend logs `Now listening on:`.

---

## G1 — count_records now wired to SA

This morning SA said *"at least 1 record"* five times in a row using
`get_records(top=1)`. Commit `6b07431` added `CountRecordsTool` to SA's
toolbelt and a hard-rule routing line in the prompt.

- [ ] **PASS / FAIL**
- **Steps:** in a fresh thread, type *"How many records are on the Gift table?"*
- **Expected:**
  1. SA's Diagnostics panel shows a single `CountRecords` tool call with `entitySetName: "eppc_gifts"`.
  2. SA's reply quotes the exact count — e.g. *"The eppc_gift table contains 11 records."* — NOT "at least N" and NOT "1 or more."
  3. One turn, one tool call, exact answer. No back-and-forth.

## G2 — SecurityBuilder error now surfaces verbatim

This morning SB returned `Function failed.` with no detail when
`get_privileges_for_table('eppc_gift')` errored. Commit `6b07431` wraps
the tool so the real Dataverse HTTP error reaches the agent.

- [ ] **PASS / FAIL**
- **Steps:** *"Create a security role called 'Forge G2 Reviewer' with Read on eppc_gift at BU depth and Write on eppc_gift at User depth."*
- **Expected — happy path:** SB gets a valid privilege list, plans the role, approval card, you approve, role created, Tester verifies PASS. **If this happens, G2 is PASS by virtue of the call succeeding.**
- **Expected — error path (still possible):** SB's chat output should NOW look like:
  ```
  get_privileges_for_table('eppc_gift') returned an error:
  ```
  <ACTUAL DATAVERSE ERROR MESSAGE HERE — e.g. HTTP 403, "Principal is missing prvReadEntity..." etc.>
  ```
  Possible next steps: (a) ... (b) ... (c) ...
  ```
  → If you see this, **paste me the actual error message** from inside the code block. That's the diagnostic I need to fix the underlying call.

- **What FAIL looks like:** SB says *"Function failed."* with no specific HTTP error quoted. If you see that, N2 has regressed.

## G3 — AutomationBuilder full management lifecycle

Independent of the SB stuff. Exercise the AB surface end-to-end on a real
workflow you can safely modify.

- [ ] **PASS / FAIL**
- **Prerequisite:** From your morning's F1 list, pick one **Unmanaged** business rule or BPF. The list showed *"Custom Bpf (BusinessProcessFlow) - opportunity - Activated, Unmanaged"* — that's a candidate. (If you don't want to touch that one, create a fresh test rule in the maker portal first.)
- **Steps (one at a time, in this order):**
  1. *"Rename the [your chosen workflow] to 'G3 Test Renamed'."*
     - Expected: AB plans → approval card → execute_update_workflow → Tester PASS.
  2. *"Deactivate G3 Test Renamed."*
     - Expected: direct deactivate_workflow call (no approval needed). Reports success.
  3. *"Activate it again."*
     - Expected: direct activate_workflow call. Reports success.
  4. *"Now delete it."*
     - Expected: **REFUSED** with *"Workflow is activated. Call deactivate_workflow first."*
  5. *"Deactivate then delete."*
     - Expected: AB chains both in one turn. Workflow gone from maker portal.
  6. *"Create a new business rule from scratch on Account."*
     - Expected: AB **refuses** with the documented *"Creating Business Rules from scratch via the Web API is not in v1 scope — build it in the maker portal first"* message. Does NOT attempt a plan_create tool.

## G4 — SecurityBuilder delete safety pre-flight

**Conditional on G2 happy-path:** only runs if G2 succeeded in creating the role.

- [ ] **PASS / FAIL** / **SKIPPED** (mark SKIPPED if G2 failed)
- **Steps:**
  1. In the maker portal: assign "Forge G2 Reviewer" to your user (Settings → Security → Users → your user → Manage Roles).
  2. In chat: *"Delete the Forge G2 Reviewer role."*
     - Expected: **REFUSED** with *"1 active user/team assignment(s)"* — clear error, role NOT deleted.
  3. In the maker portal: remove the role from your user.
  4. In chat: *"Now delete it."*
     - Expected: pre-flight count = 0 → delete fires → success report → role gone from maker portal.

---

## Cleanup after the run

- Delete `Forge G2 Reviewer` role if it survived G4 step 1.
- The renamed workflow from G3 should already be gone after step 5; if not, deactivate + delete in the maker portal.

---

## Debrief — fill in and paste back

**Overall verdict:** PASS / PARTIAL / FAIL
**Tests run:** ___/4

**Per-test status:**
- G1 (count_records): ☐ PASS  ☐ FAIL  — exact count returned? _____
- G2 (SB error surfacing): ☐ PASS happy-path  ☐ PASS error-path  ☐ FAIL  — if error-path, paste the verbatim error here:
  ```
  <paste it here>
  ```
- G3 (AB lifecycle): ☐ PASS all 6 substeps  ☐ PARTIAL (which substeps?)  ☐ FAIL  — _____
- G4 (SB delete safety): ☐ PASS  ☐ SKIPPED (G2 didn't reach happy path)  ☐ FAIL  — _____

**Surprises (anything weird that doesn't fit PASS/FAIL):**
- _

**Maker-portal state after run:**
- _

---

## Cheat-sheet — what each fix is

| Test | Fix commit | What it was | What it is now |
|---|---|---|---|
| G1 count_records | `6b07431` | SA tried get_records(top=1), reported "at least N" | SA has CountRecordsTool + prompt rule routing "how many" → count_records |
| G2 error surfacing | `6b07431` | Tool exceptions swallowed as "Function failed." | GetPrivilegesForTableTool returns PrivilegesForTableResult{Success, ErrorMessage, Privileges}; agent quotes ErrorMessage verbatim |
| G3 AB lifecycle | `521be4f` (Phase 7 Commit C) | Originally couldn't be tested — SA didn't route to AB | F1 confirmed AB routing works; G3 exercises the full management surface |
| G4 SB delete safety | `c6744f8` (Phase 6 Commit 3) | Blocked because G2 prevented role creation | Once G2 succeeds, the assigned-user pre-flight should refuse delete cleanly |
