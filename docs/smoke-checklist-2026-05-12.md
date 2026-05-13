# Real-Dataverse Smoke Checklist — 2026-05-12

Covers everything shipped since the last real-environment run: SecurityBuilder
v1 (Phase 6), AutomationBuilder v1 lifecycle (Phase 7), Tester gap fixes,
plus the backend must-fixes. Roughly **60 minutes** if you run end-to-end;
sections are independent so you can skip if time-constrained.

## How to use

Each test has an **ID** (e.g. `SB-3`), a one-line **goal**, ordered **steps**
(type these into the chat verbatim where quoted), an **expected** outcome,
and a place to record **PASS / FAIL / notes**.

On a FAIL, capture:
1. The chat transcript fragment that produced the failure.
2. The relevant backend log lines (the API window scrolling output).
3. A maker-portal screenshot if the artifact is wrong in Dataverse.

Paste the filled-in checklist back to me when done.

---

## Pre-flight (5 min)

### PF-1 — Backend starts clean
- [ ] **PASS / FAIL**
- **Steps:** stop any running backend, `dotnet build backend/DataverseForge.slnx`, then `dotnet run --project backend/src/DataverseForge.Api`.
- **Expected:** build 0 warnings / 0 errors; backend listens on its port; `/healthz` returns 200 with Dataverse + AzureAI sections.

### PF-2 — Frontend starts clean
- [ ] **PASS / FAIL**
- **Steps:** `cd frontend && npm run dev` (or use `run.bat`). Open `http://localhost:3000`.
- **Expected:** page loads in <5s; no console errors; AutomationBuilder appears in the Agents panel as **Active** (not "Not supported").

### PF-3 — Settings round-trip + ChatClientFactory disposal sanity
- [ ] **PASS / FAIL**
- **Covers:** commit `33a6593` (ChatClientFactory disposal).
- **Steps:** Open Settings → click **Test** on Azure AI + Dataverse → both green → click **Save Settings** **twice** in a row.
- **Expected:** both tests pass; no backend log warnings like "leaked connection pool" or "ObjectDisposedException" after the second save.

---

## Regression sanity (10 min) — existing tools still work

Quick check that nothing in the older surface broke during the recent work.

### RS-1 — DMB still creates a field
- [ ] **PASS / FAIL**
- **Steps:** in a fresh project, select a custom test table → ask: *"Add a string field called Smoke Test Field, max length 200, on the [table name] table."*
- **Expected:** SA hands off to DMB → DMB plans → approval card → execute → Tester verifies → PASS. Field appears in maker portal.

### RS-2 — UIBuilder still creates a view
- [ ] **PASS / FAIL**
- **Steps:** *"Create a saved view on [table name] called 'Smoke Test View' showing the primary name column."*
- **Expected:** UIBuilder plans → approval → execute → Tester PASS. View appears in maker portal.

### RS-3 — Backend GetRecordCount surfaces 4xx body
- [ ] **PASS / FAIL**
- **Covers:** commit `04d3378` (`EnsureSuccessOrSurfaceAsync` on count).
- **Steps:** ask SA *"How many records are on a table called 'does_not_exist'?"*
- **Expected:** SA reports a clear error message containing the Dataverse OData error body (not a generic "HTTP 400 Bad Request").

---

## SecurityBuilder smoke (20 min)

**Prerequisites:** pick a table with at least a Read/Write/Append privilege —
the existing `account` table works, or any custom table you created earlier.

### SB-1 — Create role with privilege matrix
- [ ] **PASS / FAIL**
- **Covers:** commit `92891ce` (SB Commit 1).
- **Steps:** *"Create a security role called 'Forge Smoke Reviewer' with Read on Account at BU depth and Write on Account at User depth."*
- **Expected:**
  1. SA hands off to SecurityBuilder.
  2. SB calls `get_business_units` (you should see this in the diagnostics panel).
  3. SB calls `get_privileges_for_table('account')`.
  4. SB calls `plan_create_security_role` → approval card appears with risk = orange.
  5. You approve → `execute_create_security_role` runs → reports RoleId.
  6. SB hands off to Tester → `verify_security_role_against_plan` returns **PASS**.
- **Verify in maker portal:** Settings → Security → Security Roles → "Forge Smoke Reviewer" exists, Account → Read at BU depth, Write at User depth.

### SB-2 — Bulk privilege grant across 2-3 roles
- [ ] **PASS / FAIL**
- **Covers:** commit `da8e3ec` (bulk grant).
- **Steps:**
  1. *"List existing security roles named like 'Forge Smoke'."* (Should return SB-1's role.)
  2. Create a second role: *"Create a security role 'Forge Smoke Lite' with Read on Account at User depth."*
  3. *"Give both the Forge Smoke Reviewer and Forge Smoke Lite roles Append on Account at BU depth."*
- **Expected:**
  1. SB calls `plan_grant_privilege_to_roles` with both roleIds.
  2. Risk = orange, approval card lists both role names.
  3. After approval, `execute_grant_privilege_to_roles` reports "Granted to 2 role(s)".
  4. Tester runs `verify_bulk_privilege_grant_against_plan` → OverallVerdict = PASS, per-role results both PASS.
- **Verify in maker portal:** Both roles now have Append on Account at BU depth.

### SB-3 — Update via privilege delta (add + remove + rename)
- [ ] **PASS / FAIL**
- **Covers:** commit `7e657b7` (SB Commit 2).
- **Steps:** *"On the Forge Smoke Reviewer role: add Delete on Account at BU depth, remove the Write privilege on Account, and rename the role to 'Forge Smoke Reviewer V2'."*
- **Expected:**
  1. SB reads live role + live privileges first.
  2. `plan_update_security_role` returns: PrivilegesToAdd=[Delete@BU], PrivilegesToRemove=[Write@User]. Rename queued.
  3. Risk = orange (has removes).
  4. After approval, execute does PATCH (rename) → AddPrivilegesRole (Delete) → RemovePrivilegeRole (Write).
  5. Tester verifies post-state matches the target spec.
- **Verify in maker portal:** Role renamed, Read+Append+Delete on Account but not Write.

### SB-4 — Delete refused while assigned
- [ ] **PASS / FAIL**
- **Covers:** commit `c6744f8` (SB Commit 3 — safety pre-flight).
- **Steps:**
  1. In the maker portal, assign "Forge Smoke Reviewer V2" to your own user (Settings → Security → Users → your user → Manage Roles).
  2. Back in the chat: *"Delete the Forge Smoke Reviewer V2 role."*
- **Expected:** SB calls `delete_security_role` → tool refuses with **"1 active user/team assignment(s)"** in the error message + a maker-portal pointer. The role still exists in the env.

### SB-5 — Delete succeeds after unassign
- [ ] **PASS / FAIL**
- **Steps:**
  1. In the maker portal, remove the role from your user.
  2. Chat: *"Now delete the Forge Smoke Reviewer V2 role."*
- **Expected:** Pre-flight count = 0 → delete fires → success report. Role gone from maker portal.

### SB-6 — Undo CreateSecurityRole round-trip
- [ ] **PASS / FAIL**
- **Covers:** commit `c6744f8` (undo dispatcher case).
- **Steps:** *"Undo the last operation."* (this should now point at the role-delete that just happened OR an earlier role-create depending on stack order — adjust the prompt if needed: *"List undoable operations"* first, then pick the SB-1 create explicitly).
- **Expected:** Undo for "ExecuteCreateSecurityRole" calls `DeleteRoleAsync` → the original role is gone. Verify the maker portal: no Forge Smoke roles remain. *(Note: undo of a create deletes the artifact; it doesn't restore privileges if you then re-create.)*

### SB-7 — RoleTemplate option exposed (read only)
- [ ] **PASS / FAIL**
- **Covers:** commit `1dd1570` (RoleTemplate binding).
- **Steps:** *"List the available role templates."*
- **Expected:** SB calls `get_role_templates` → returns the BU-replication templates Dataverse exposes (typically several Microsoft-shipped templates). The agent should also explicitly mention that creating a role bound to one of these will auto-replicate it across new BUs.

---

## AutomationBuilder smoke (15 min)

**Prerequisites:** at least one existing Business Process Flow OR Business
Rule in your environment. If you have none, create a 1-stage Business Rule in
the maker portal first (Solutions → table → Business Rules → New → simple
"if X then show Y") and activate it. The smoke covers the **management
surface**; creation is out of scope for v1.

### AB-1 — List workflows
- [ ] **PASS / FAIL**
- **Covers:** commit `275179d` (`GetWorkflowsAsync`).
- **Steps:** *"List all the business process flows and business rules in this environment."*
- **Expected:** SA hands off to AutomationBuilder → AB calls `get_workflows` → returns at least the rule/flow you just created. Each row shows name, primary entity, category, activation state, and IsManaged flag.

### AB-2 — Rename a business rule
- [ ] **PASS / FAIL**
- **Covers:** commit `521be4f` (PlanUpdate + ExecuteUpdate).
- **Steps:** *"Rename the [name of your test rule] business rule to 'Smoke Test Renamed Rule'."*
- **Expected:**
  1. AB reads the workflow (refuses immediately if it turns out to be managed).
  2. `plan_update_workflow` returns a `update_business_rule_*` planId.
  3. Risk = yellow (rename is low-risk).
  4. Approval → `execute_update_workflow` does PATCH on /workflows(id).
  5. Tester verifies via `verify_workflow_against_plan` → PASS (name matches).
- **Verify in maker portal:** rule renamed.

### AB-3 — Deactivate / activate round-trip
- [ ] **PASS / FAIL**
- **Covers:** commit `521be4f` + the SetState pattern.
- **Steps:**
  1. *"Deactivate the Smoke Test Renamed Rule."* → expect direct call (no approval card needed for this).
  2. *"Now activate it again."*
- **Expected:**
  1. `deactivate_workflow` → success report.
  2. `activate_workflow` → success report.
  3. Both push OperationStack entries marked as reversible.
- **Verify in maker portal:** rule shows as Activated after step 2.

### AB-4 — Delete refused on activated rule
- [ ] **PASS / FAIL**
- **Steps:** *"Delete the Smoke Test Renamed Rule."* (rule should still be activated from AB-3).
- **Expected:** `delete_workflow` refuses with **"Workflow is activated. Call deactivate_workflow first"** message. Rule still exists.

### AB-5 — Delete succeeds after deactivate
- [ ] **PASS / FAIL**
- **Steps:** *"Deactivate it then delete it."*
- **Expected:** AB chains deactivate → delete in one turn. Rule gone from maker portal.

### AB-6 — Refusal on new-workflow creation
- [ ] **PASS / FAIL**
- **Covers:** commit `7ee9c9e` (v1 management-only scope).
- **Steps:** *"Create a new business rule on the Account table that hides the Description field when the Account Type is Customer."*
- **Expected:** AB refuses with the documented message ("Creating Business Rules from scratch via the Web API is not in v1 scope — build it in the maker portal first, then I can manage it from here"). It does **NOT** attempt to call a `plan_create_business_rule` tool (the tool doesn't exist; the agent should know this).

### AB-7 — Undo activate
- [ ] **PASS / FAIL**
- **Covers:** commit `521be4f` (undo dispatcher cases).
- **Steps:** create another test rule in maker portal → activate it via chat (`AB-3` style) → then *"Undo the last operation."*
- **Expected:** Undo for "ActivateWorkflow" calls `DeactivateWorkflowAsync` → maker portal confirms rule is deactivated.

---

## Tester smoke (10 min)

### T-1 — VerifyFormAgainstPlan fires when UIBuilder creates a form
- [ ] **PASS / FAIL**
- **Covers:** commit `cfd9e45` (Form/View plan-keyed verifiers).
- **Steps:** *"Create a new Main form on [table] called 'Smoke Form' with the primary name column on it."*
- **Expected:** UIBuilder plans → execute → handoff to Tester → Tester calls `verify_form_against_plan` (not the legacy `verify_form_creation`) → PASS with the planId-keyed payload comparison. The diagnostics panel shows `verify_form_against_plan` was invoked.

### T-2 — VerifyViewAgainstPlan fires when UIBuilder creates a view
- [ ] **PASS / FAIL**
- **Steps:** *"Create a saved view on [table] called 'Smoke Plan View' showing the primary name and createdon columns."*
- **Expected:** Same as T-1 but with `verify_view_against_plan`. Diagnostics panel shows the new tool.

### T-3 — DataPopulation hard-handoff to Tester
- [ ] **PASS / FAIL**
- **Covers:** commit `cfd9e45` (DataPop HANDOFF RULE elevation).
- **Steps:** *"Generate 5 sample test records on [table]."*
- **Expected:** DataPopulation runs `execute_generate_test_data` → **automatically** hands off to Tester (same turn) → Tester runs `count_records` + `get_records(top=3)` → reports verification. **No need to ask "verify it" as a follow-up.** This is the change that should have shipped.

### T-4 — Tester ignores "Approved" messages routed wrong
- [ ] **PASS / FAIL**
- **Covers:** commit `cfd9e45` (Tester "ignore Approved" rule).
- **Steps:** Trigger any approval flow (e.g. ask DMB to create a field) → on the approval card, click **Approve**. Watch the Tester turn after execution.
- **Expected:** When Tester wakes up, its FIRST action is a `verify_*_against_plan` tool call (looking at the most recent execute_* result), NOT a text reply saying "I noted your approval" or "thanks for approving". The diagnostics panel should show the tool fired before any user-visible text.

---

## Per-agent model assignment (optional, 5 min)

### MODEL-1 — Override SB to a different model
- [ ] **PASS / FAIL** / **SKIPPED**
- **Steps:**
  1. Stop the backend.
  2. Edit `backend/src/DataverseForge.Api/bin/Debug/net10.0/user-settings.json` (or wherever your settings live) — add: `"AzureAI": { "AgentModels": { "SecurityBuilder": "<a different model name you have>" } }`. Or use the Settings UI if it exposes this — check.
  3. Restart backend.
  4. Trigger a SB action.
- **Expected:** Backend log shows SB using the override model, other agents using DefaultModel. Skip if you don't have a second model deployed.

---

## Cleanup (2 min)

If anything from the smoke is still in your environment:
- Delete `Forge Smoke Reviewer` / `Forge Smoke Lite` / `Forge Smoke Reviewer V2` roles (if any survived).
- Delete `Smoke Test Field` from your test table.
- Delete `Smoke Test View` / `Smoke Plan View` saved views.
- Delete `Smoke Form` form (or leave it — it's harmless).
- Delete any test business rules.

---

## Debrief — fill this in and paste back

**Overall verdict:** PASS / PARTIAL / FAIL
**Tests run:** ___/30 (count from `[ ]` checkboxes above)
**Tests passed:** ___
**Tests failed:** ___
**Tests skipped:** ___

**Failures (one line each, with the test ID):**
- e.g. `SB-3 — FAIL — execute_update_security_role 400'd on rename PATCH; backend log shows If-Match header rejected. Suspect: workflow PATCH path bleeding into role PATCH.`

**Unexpected behavior worth flagging (not a clear PASS/FAIL):**
- e.g. `AB-1 — listed 47 workflows but 3 of them showed category=0 (classic workflow). Filter wasn't tight enough?`

**Maker-portal state after smoke:**
- e.g. `2 test roles still exist (couldn't delete because of stuck assignment). Other artifacts cleaned up.`

**Performance / latency notes (optional):**
- e.g. `SB-3 update took 18s — felt slow. Approval card took ~6s to render.`

---

## Cheat-sheet — what each commit needs to prove

| Commit | Smoke tests covering it |
|---|---|
| `04d3378` backend must-fixes | RS-3 |
| `cfd9e45` Tester gaps | T-1, T-2, T-3, T-4 |
| `33a6593` ChatClientFactory disposal | PF-3 |
| `92891ce` SB Commit 1 (create) | SB-1 |
| `7e657b7` SB Commit 2 (update delta) | SB-3 |
| `c6744f8` SB Commit 3 (delete + undo) | SB-4, SB-5, SB-6 |
| `da8e3ec` Bulk grant | SB-2 |
| `1dd1570` RoleTemplate | SB-7 |
| `86b72b7` Frontend agentDefinitions | PF-2 |
| `275179d` AB Commit A (infrastructure) | AB-1 |
| `d79fd1d` AB Commit B (scaffold) | AB-1 |
| `521be4f` AB Commit C (lifecycle) | AB-2, AB-3, AB-4, AB-5, AB-7 |
| `7ee9c9e` AB Commit D (scope finalized) | AB-6 |
| `7b4f212` Comment cleanup | — (no behavior change; covered by all of the above) |
