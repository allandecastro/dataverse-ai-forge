# Handoff — Resume 2026-05-14

Stopping point at end of 2026-05-13 evening session. Pick this up tomorrow.

---

## Where we are

### What shipped + pushed today

| Commit | What |
|---|---|
| `90d6174` | Settings save no longer wipes Azure AI on empty form-key (PUT /api/settings now reads endpoint+key+model from the freshly-saved file, never from the DTO). |
| `5879f37` | (a) `GetPrivilegesForTableAsync` uses `$select=LogicalName,Privileges` instead of `$expand=Privileges` — Privileges is a property, not a nav property. (b) `PseudoHandoffRecovery` narrative regex extended to include `security_builder` + `automation_builder` (Bug #2 + Bug #4). |
| `8430523` | (a) `VerifySecurityRoleAgainstPlan` no longer FAILs on Dataverse auto-granted baseline privileges (SharePointData, SdkMessage, etc.). (b) `AddPrivilegesRole` body aligned with MS Learn example (dropped `@odata.type`). (c) SA prompt anti-example F added — never claim "successfully created" on Tester FAIL. |

### What's in tree but NOT committed yet (must commit + push tomorrow first thing)

- `backend/src/DataverseForge.Infrastructure/Dataverse/DataverseClient.RoleWrites.cs`
  - **Diagnostic + retry** in `AddPrivilegesToRoleAsync`:
    1. POST with Depth as **string** ("Local")
    2. Readback via `RetrieveRolePrivilegesRole`
    3. If any privilege has the wrong depth → log warning + retry the mismatched ones with Depth as **int** (0/1/2/3)
    4. Final readback → log SUCCESS or persistent ERROR
  - Logging is **unconditional** (not gated on `VerboseLogging`) so the first G2 run after this lands gives us the answer.

Untracked file: this doc (`docs/handoff-2026-05-14-resume.md`).

---

## The open bug: depth Local → Basic

### Symptom (real env, 2026-05-13 21:47 + 21:59)

User asks: "Create a security role 'Forge GX Reviewer' with Read on eppc_gift at BU depth and Write on eppc_gift at User depth".

- SB creates role and calls `AddPrivilegesRole` with `Depth: "Local"` for prvReadeppc_Gift.
- Tester reads via `RetrieveRolePrivilegesRole` → comes back `"Basic"`.
- Tester verdict FAIL: "expected Local but actual Basic".

### Hypotheses (rank-ordered)

1. **Dataverse baseline auto-grant conflicts with AddPrivilegesRole** — when a role is created, Dataverse pre-grants some privileges at Basic depth as baseline. AddPrivilegesRole may no-op on already-present privileges instead of replacing depth. The SDK doc says "replace depth" but the Web API action may differ.
2. **OData enum parsing** — `"Depth": "Local"` may be misparsed by Dataverse (enum default = 0 = Basic). The MS Learn example uses the string form but the int form may be what's actually accepted server-side.
3. **Privilege metadata lies** — `CanBeLocal: true` on the privilege but the table's ownership type (UserOwned) prevents Local at the storage layer, and Dataverse downgrades silently.

### The diagnostic that's in tree

After tomorrow's `git push`, the next G2 run will produce these log lines:

```
AddPrivilegesRole starting: role={RoleId}, count=2
AddPrivilegesRole body (role={RoleId}, depthForm=string): {"Privileges":[{"PrivilegeId":"...","Depth":"Local"}, ...]}
AddPrivilegesRole depth mismatch after string-Depth attempt: privilege prvReadeppc_Gift (...) asked=Local actual=Basic
AddPrivilegesRole: retrying 1 mismatched privilege(s) with Depth as int on role {RoleId}
AddPrivilegesRole body (role={RoleId}, depthForm=int): {"Privileges":[{"PrivilegeId":"...","Depth":1}, ...]}
AddPrivilegesRole: int-Depth retry SUCCEEDED for prvReadeppc_Gift (...) → Local
```

OR (if int doesn't fix it either):
```
AddPrivilegesRole STILL wrong after int-Depth retry: privilege prvReadeppc_Gift (...) asked=Local actual=Basic. Likely a Dataverse baseline-privilege conflict or table-ownership constraint — neither wire shape works.
```

### Decision tree based on what the log shows

| Log signal | Conclusion | Fix |
|---|---|---|
| string-Depth attempt → ALL at requested depth | Bug was already fixed by `8430523` (dropping `@odata.type`). Just remove the diagnostic + retry, ship a clean version. | Strip the retry path, keep the simple version. |
| string-Depth attempt fails, int-Depth retry succeeds | Hypothesis 2 confirmed: Dataverse needs int on the wire even though docs show string. | Make `int` the default; drop the string-first attempt; document why. |
| Both string AND int fail → "STILL wrong" log | Hypothesis 1 or 3: Dataverse baseline-grant or ownership-type conflict. | Try `RemovePrivilegeRole` first, then `AddPrivilegesRole`. Or use `ReplacePrivilegesRole` (replaces full set). Or accept the limitation and document. |

---

## How to resume tomorrow (concrete steps)

1. **Stop backend** (Ctrl+C in its terminal). It was running PID 50952 at end-of-day.
2. **Commit + push the uncommitted diagnostic change**:
   ```powershell
   git add backend/src/DataverseForge.Infrastructure/Dataverse/DataverseClient.RoleWrites.cs docs/handoff-2026-05-14-resume.md
   git commit -m "diag(backend): AddPrivilegesRole readback + int-Depth retry to nail down the Local→Basic bug"
   git push
   ```
3. **Rebuild + restart backend**:
   ```powershell
   dotnet build backend/DataverseForge.slnx
   dotnet run --project backend/src/DataverseForge.Api
   ```
4. **In the maker portal**: delete any leftover `Forge G2 Reviewer` and `Forge G3 Reviewer` roles from yesterday's runs so the duplicate-name guard doesn't reject the next attempt.
5. **In the chat**:
   - Confirm environment + use EPPCLiveDemo solution.
   - "Create a security role called 'Forge Day2 Reviewer' with Read on eppc_gift at BU depth and Write on eppc_gift at User depth."
   - Approve when SB asks.
6. **Watch the backend terminal** for the diagnostic lines listed above.
7. **Apply the right fix** based on the decision tree.

---

## Open items that are NOT depth-related

- **Bug C verification** — SA prompt anti-example F was added in `8430523` for the "successfully created on a FAIL" case, but yesterday's transcript cut off before we saw SA respond to the Tester FAIL. After tomorrow's run, watch SA's reply: it MUST surface the FAIL discrepancies verbatim, not say "successfully created". If it still pretends success, the prompt fix isn't strong enough — escalate to a code-level guard (compare Tester verdict vs SA's next message and warn).
- **Bug 3 (sanitizer leak)** — punted yesterday. SA at 21:14:01 produced a handoff message concatenated with its previous environment-info reply. No code-level fix proposed yet. Status: known-open, not blocking.
- **Smoke checklist** — `docs/smoke-rerun-2026-05-13-pass2.md` G1 ✅ PASS, G2 partial (Bug A blocks happy-path), G3 ✅ PASS, G4 still blocked on G2. Once G2 happy-path works, G4 (delete safety with assigned user) becomes testable.

- **AutomationBuilder — creation testing** (Allan's ask, end-of-day 2026-05-13):
  Two possible interpretations, pick one tomorrow:

  - **(A) Test the documented refusal** — current AB v1 scope is management-only
    (rename / activate / deactivate / delete) per `7ee9c9e` and UNSUPPORTED.md.
    G3 step 6 in the smoke plan ("Create a new business rule from scratch on
    Account") expects AB to refuse with the documented "Creating Business
    Rules from scratch via the Web API is not in v1 scope — build it in the
    maker portal first" message. This wasn't exercised in yesterday's run.
    Quick (2 min): ask the agent "Create a new business rule on Account" and
    verify AB refuses with that copy + no `plan_create_workflow` tool call
    fires. Same for "Create a new BPF on opportunity". If AB attempts
    anything, that's a regression.

  - **(B) Build out the creation surface** (Phase 7 v2) — non-trivial. Need
    Business Rule XAML + BPF XAML template generation, schema validation
    against a real env, the plan/execute split, Tester verification. The
    deferral reason on `7ee9c9e` was specifically "until XAML templates can
    be validated against a real Dataverse env" — that real-env access is
    what we now have. Estimate: 1-2 days for Business Rules alone (BPFs are
    a separate workstream because the XAML schema is different).

  Default to (A) for tomorrow unless we're explicitly committing to (B).
  If (B), open a Phase 7 v2 plan doc first and stage it as a new commit
  series rather than folding into the depth-bug fix.

---

## Quick refs

- Wire shape doc (MS Learn): https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/addprivilegesrole
- The two relevant files:
  - [DataverseClient.RoleWrites.cs](../backend/src/DataverseForge.Infrastructure/Dataverse/DataverseClient.RoleWrites.cs) — `AddPrivilegesToRoleAsync` + `ReadRolePrivilegeDepthsAsync` + `MapDepthEnum`
  - [VerifySecurityRoleAgainstPlanTool.cs](../backend/src/DataverseForge.Application/Tools/VerifySecurityRoleAgainstPlanTool.cs) — verifier that surfaces the FAIL
