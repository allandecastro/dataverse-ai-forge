# Smoke Re-run Checklist — 2026-05-13

Verifies the seven fixes shipped in commits `c0d237e` and `e58159c` against
the failures surfaced in the 2026-05-13 morning smoke. Roughly **30 minutes**
end-to-end — much narrower than the original `smoke-checklist-2026-05-12.md`
because we only need to retest the regressions and a handful of new tests
targeting the discipline rules.

If anything still fails, capture: chat transcript, the **Diagnostics panel**
(tool calls + their results) for the failing turn, and the backend log lines
from the same window. Paste the filled-in debrief back to me.

## Before you start

Both backend + frontend must be restarted from clean — the prompt + verifier
changes only apply after a backend rebuild.

```powershell
# in the backend window (Ctrl+C if it's running)
cd C:\Users\allan.de.castro\Documents\GitHub\dataverse-ai-forge\backend
dotnet build DataverseForge.slnx
dotnet run --project src/DataverseForge.Api
```

Frontend doesn't need a restart — hard-refresh (Ctrl+Shift+R) once the
backend is up.

---

## Fix verifications

### F1 — B1 fix: SA routes BPF / Business Rule asks to AutomationBuilder

This was the worst failure of the morning — SA narrated "Handing off to
Automation Builder" five times in a row without ever firing the handoff
tool. Routing table was stale.

- [ ] **PASS / FAIL**
- **Steps:** in a fresh thread, type *"List all the business process flows and business rules in this environment."*
- **Expected:**
  1. SA hands off to **AutomationBuilder** on the **first** message — the Diagnostics panel shows a `HandoffToAutomationBuilder` tool call. If you see SA's reply but no tool call in Diagnostics, F1 has regressed.
  2. AutomationBuilder calls `get_workflows` and returns the BPF + Business Rule list.
- **What to check in Diagnostics:** the tool call list for SA's turn must include `HandoffToAutomationBuilder` (not just text saying "handing off").

### F2 — B2 fix: count_records works (no `Invalid $top` error)

The previous count_records URL had `?$count=true&$top=0`, which his env
rejected. We dropped `$top=0`.

- [ ] **PASS / FAIL**
- **Steps:** *"How many records are on the Gift table?"*
- **Expected:** SA calls `count_records(entitySetName: "eppc_gifts")` or routes via DataPopulation → number returned. No "Invalid value for $top query option" error.

### F3 — B4 fix: view creation no longer false-FAILs on fetchxml diff

Previous run: Tester reported FAIL because the planned fetchxml (223 chars)
didn't match Dataverse's echoed version (275 chars — Dataverse injected
`savedqueryid`). The verifier now strips that attribute.

- [ ] **PASS / FAIL**
- **Steps:** *"Create a saved view on Gift table called 'Smoke Rerun View' with the primary name and createdon columns."*
- **Expected:**
  1. UIBuilder plans → approval card.
  2. You approve → execute creates the view.
  3. Tester verifies → verdict = **PASS** (not FAIL).
  4. **Crucially:** Tester writes exactly **ONE** verification report and **ONE** handoff back to SA. If you see Tester reporting more than once, B3 has also regressed.

### F4 — B3 fix: Tester stops after a handoff-rejected response

This one is hard to trigger deliberately now that B4 is fixed (most FAILs
should now be PASS). Quick test: force a verifier FAIL by mismatching the
viewId.

- [ ] **PASS / FAIL** / **SKIPPED** (if F3 PASSed cleanly with one report, B3 is implicitly validated)
- **Steps:** If F3 produced multiple Tester reports, F4 FAILED. If F3 produced one, F4 PASSED.

### F5 — B5 fix: SA doesn't hallucinate table names

Morning's failure: *"How many records on a table called Toto?"* → SA invented
the table name `toto_gift` and claimed 0 records, no tool call.

- [ ] **PASS / FAIL**
- **Steps:** *"How many records are on a table called Toto?"*
- **Expected:**
  1. SA calls `get_tables_list(searchTerm: "toto")` **first** — verify in Diagnostics.
  2. The list returns nothing (or unrelated matches).
  3. SA replies plainly: *"I don't see a table called 'Toto' in this environment"* and offers to clarify. **Does NOT** invent a logical name. **Does NOT** report a record count from nothing.

### F6 — B6 fix: SecurityBuilder retry on a custom table, with verbose error on failure

Morning's failure: `get_privileges_for_table('account')` returned generic
"Function failed." twice. Likely a permission issue on Account specifically.
Test with a custom table to rule out env permissions; if it still fails, the
agent should now quote the error verbatim + suggest concrete diagnostic steps.

- [ ] **PASS / FAIL**
- **Steps:** *"Create a security role called 'Forge Rerun Reviewer' with Read on eppc_gift at BU depth and Write on eppc_gift at User depth."*
- **Expected (happy path):**
  1. SecurityBuilder calls `get_business_units` + `get_privileges_for_table('eppc_gift')` — both succeed.
  2. `plan_create_security_role` → approval card (risk: orange).
  3. You approve → role created with the two privileges.
  4. Tester verifies → PASS.
- **Expected (failure path — if get_privileges_for_table still errors):** SB now quotes the verbatim error in a fenced code block AND offers 2-3 concrete next steps (try a different table, check SP permissions, etc.). Compare to the morning's "I encountered an error trying to fetch privileges" — that wording is now forbidden.

### F7 — Optional: AutomationBuilder full management lifecycle

If F1 PASSed, exercise the rest of the AB surface to confirm it's wired correctly.

- [ ] **PASS / FAIL** / **SKIPPED**
- **Steps (one at a time):**
  1. *"Rename [name of a test business rule from F1's list] to 'Smoke Rerun Renamed Rule'."* → expect plan → approval → execute → Tester PASS.
  2. *"Deactivate that rule."* → direct call, no approval needed.
  3. *"Now activate it."* → direct call. 
  4. *"Delete that rule."* → expect REFUSED because activated. Then *"Deactivate then delete."* → success.
  5. *"Create a new business rule from scratch."* → expect AB to **refuse** with the documented "build it in the maker portal first" message. (Verifies the management-only scope.)

### F8 — Optional: SB delete safety pre-flight (only if F6 PASSed)

- [ ] **PASS / FAIL** / **SKIPPED**
- **Steps:**
  1. In maker portal: assign "Forge Rerun Reviewer" to your user.
  2. Chat: *"Delete the Forge Rerun Reviewer role."* → expect REFUSED with "1 active user/team assignment(s)".
  3. Unassign in maker portal.
  4. Chat: *"Now delete it."* → success.

---

## Cleanup

After the re-run:
- Delete `Smoke Rerun View` saved view (or leave; harmless).
- Delete `Forge Rerun Reviewer` security role.
- Delete the renamed `Smoke Rerun Renamed Rule` business rule (if F7 left it).

---

## Debrief — fill in and paste back

**Overall verdict:** PASS / PARTIAL / FAIL
**Tests run:** ___/8
**Tests passed:** ___
**Tests failed:** ___

**Per-fix status:**
- B1 (F1): ☐ PASS  ☐ FAIL  — _____
- B2 (F2): ☐ PASS  ☐ FAIL  — _____
- B3 (F4): ☐ PASS  ☐ FAIL  ☐ SKIPPED  — _____
- B4 (F3): ☐ PASS  ☐ FAIL  — _____
- B5 (F5): ☐ PASS  ☐ FAIL  — _____
- B6 (F6): ☐ PASS  ☐ FAIL  — _____
- B7 (covered by F3 — if PARTIAL surfaced over FAIL on a content diff, ☐ PASS)

**Failures with diagnostics:**
- e.g. `F1 — FAIL — SA still narrated "handing off" without firing the tool. Diagnostics for SA's turn shows zero tool calls. Backend log shows the assistant message but no orchestrator handoff event.`

**New surprises (not on the test list):**
- _

**Maker-portal state after run:**
- _

---

## Cheat-sheet — what each fix is

| Bug | Fix commit | What it was | What it is now |
|---|---|---|---|
| B1 SA → AB routing | `c0d237e` | SA narrated "handing off to AB" but tool never fired | Routing table lists explicit `handoff_to_automation_builder` lines for BPF / Business Rule / workflow asks |
| B2 count_records | `c0d237e` | URL had `$top=0`, rejected by some envs | URL is `$count=true` only |
| B3 Tester loop | `c0d237e` | Tester retried handoff 10× after REJECTED response | TesterPrompt explicit "STOP on REJECTED" + HandoffTool rejection message rewritten to be unambiguous |
| B4 verifier XML strict | `c0d237e` | False-FAIL on `savedqueryid` injection | Verifier strips server-injected attributes before comparing |
| B5 SA hallucinated names | `e58159c` | SA invented `toto_gift` logical name and 0 record count | "NEVER state a name or count you didn't just read" hard rule |
| B6 generic error masking | `e58159c` | "Function failed" got paraphrased to "I encountered an error" | `QuoteToolErrorsVerbatim` extended to require quoting generic errors verbatim + 2-3 diagnostic next steps |
| B7 FAIL vs PARTIAL | `e58159c` | Tester FAIL'd on content-only XML diffs | New rule: byte-content diff + functional check passes = PARTIAL, not FAIL |
