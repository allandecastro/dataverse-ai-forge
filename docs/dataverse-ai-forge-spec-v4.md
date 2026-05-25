# Dataverse AI Forge — Master Specification v4

> **Consolidated document** incorporating all decisions from our design sessions.
> This merges: v3 base spec + v3.1 addendum (BA agent, model strategy) + 
> UI design iterations (v7–v9) + custom SSE client architecture.
> Single source of truth for the Dataverse AI Forge project.

> **⚠️ Drift notice (2026-05):** Sections 1, 2, and the persistence subsection
> of section 9 have been kept in lockstep with shipped reality. Other
> sections — notably **§6.8 (Reviewer tools)**, **§7.8 (Reviewer prompt)**,
> the handoff topology in §4, and parts of the roadmap in §12 — still
> reference the Reviewer agent and the original 10-agent count. The
> Reviewer was removed in commit `0337550` (its pre-flight checks moved
> into each builder's plan_* tools) and SecurityBuilder shipped in Phase 6,
> so the active roster is 7 (BA, SA, DMB, UIBuilder, DataPopulation,
> Tester, SecurityBuilder) with 2 roadmap items (Automation Builder,
> Documentor). When the spec disagrees with the code, the code wins —
> these stale sections are kept for historical context until a full v5
> rewrite.

---

## Table of Contents

1. [Vision & Stack](#1-vision--stack)
2. [Agent Roster (8 Active + 1 Roadmap)](#2-agent-roster-8-active--1-roadmap)
3. [Model Strategy Per Agent](#3-model-strategy-per-agent)
4. [Orchestration Design](#4-orchestration-design)
5. [Solution Onboarding Workflow](#5-solution-onboarding-workflow)
6. [Complete Tool Catalog](#6-complete-tool-catalog)
7. [Agent System Prompts](#7-agent-system-prompts)
8. [Error Recovery & Undo](#8-error-recovery--undo)
9. [Session Persistence & Project Management](#9-session-persistence--project-management)
10. [Frontend Architecture & Design System](#10-frontend-architecture--design-system)
11. [Frontend SSE Integration](#11-frontend-sse-integration)
12. [MVP Implementation Roadmap](#12-mvp-implementation-roadmap)
13. [Demo Script](#13-demo-script)
14. [Future Considerations](#14-future-considerations)

---

## 1. Vision & Stack

**Dataverse AI Forge** is an AI-powered platform that builds Dataverse solutions through natural language conversation. The user describes what they need, a team of specialized agents (8 active, 1 on the roadmap) collaborates to design, validate, create, test, secure, and automate the solution.

### Tech Stack

| Layer | Technology |
|-------|-----------|
| **Backend** | .NET 10, Clean Architecture |
| **Agent Framework** | Microsoft Agent Framework (preview) — Handoff orchestration |
| **LLM** | Azure AI Foundry (per-agent model config via appsettings.json) |
| **Dataverse** | Web API v9.2 + MSAL authentication |
| **GitHub** | Octokit.NET (branches, commits, PRs) |
| **Frontend** | React + Next.js |
| **Agent–UI Bridge** | Custom SSE Client (ForgeChatProvider) + AG-UI Protocol |
| **Real-time** | AG-UI SSE events (replaces SignalR) |
| **Persistence** | In-memory bounded LRU stores (100-session cap). SQLite + Cosmos paths were removed when no deploy ever exercised them. |
| **Observability** | ILogger console + `System.Diagnostics.Metrics` / `ActivitySource` primitives (in-process). OTLP exporter wiring was removed; consumers can re-add it without touching the agent code. |

### Key Architectural Decisions

- **Handoff orchestration** as backbone (Magentic is Python-only in Agent Framework)
- **No MCP servers** for PoC — all tools implemented as native AIFunction tools
- **plan_* / execute_* split** — every write operation goes through planning then execution
- **Risk-based approval** — auto-proceed for low-risk, require approval for high-risk
- **Shared Project State** — persistent state injected into every agent via middleware
- **Session persistence per project** — close browser, come back, resume where you left off

---

## 2. Agent Roster (8 Active + 1 Roadmap)

> **Update (Phase 7, 2026-05):** AutomationBuilder shipped in Commits A-D
> as **lifecycle management only** for Business Process Flows + Business
> Rules (rename / activate / deactivate / delete). Workflow creation
> via Web API is deferred until XAML templates can be validated against
> a real Dataverse environment — users build new BPFs / Business Rules
> in the maker portal first, then manage them via this agent.
> Documentor remains the only unbuilt agent. Reviewer was dropped in
> commit `0337550` (pre-flight checks moved into each builder's plan_*
> tools); SecurityBuilder shipped in Phase 6.

### Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│                           USER                                        │
│                            │                                          │
│                            ▼                                          │
│              ┌──────────────────────────┐                             │
│              │   BUSINESS ANALYST       │ ← NEW: First contact for    │
│              │   (Requirement Refiner)  │   ambiguous/broad requests  │
│              │   GPT-4o                 │                             │
│              └────────────┬─────────────┘                             │
│                           │ (structured requirements)                 │
│                           ▼                                          │
│              ┌──────────────────────────┐                             │
│              │   SOLUTION ARCHITECT     │                             │
│              │   (Planner/Orchestrator) │                             │
│              │   GPT-4o                 │                             │
│              └──────┬──────┬──────┬────┘                             │
│                     │      │      │                                   │
│     ┌───────────────┼──────┼──────┼───────────────┐                  │
│     │      │        │      │      │        │      │                  │
│     ▼      ▼        ▼      ▼      ▼        ▼      ▼                  │
│   ┌────┐┌────┐  ┌────┐┌────┐┌────┐  ┌────┐┌────┐┌────┐             │
│   │Data││ UI │  │Auto││Secu││Data│  │Test││Revi││Docu│             │
│   │Mod.││Bld.│  │Bld.││rity││Pop.│  │ er ││ewer││ men│             │
│   │Bld.││    │  │    ││Bld.││Agt.│  │    ││    ││ tor│             │
│   └────┘└────┘  └────┘└────┘└────┘  └────┘└────┘└────┘             │
│   4o-m   4o-m   GPT4o  4o-m  4o-m   4o-m  4o-m   4o-m              │
│                                                                      │
│   BUILDERS               SUPPORT          GOVERNANCE                 │
│   (write)                (write)          (read-only)                │
└──────────────────────────────────────────────────────────────────────┘
```

### Agent Details

| # | Agent | Status | Role | Tools | Hands Off To |
|---|-------|--------|------|-------|-------------|
| 1 | **Business Analyst** | Active | Requirement elicitation, scope refinement, structured specs | None (conversation only) | Solution Architect |
| 2 | **Solution Architect** | Active | Planner, coordinator, sole user interface | READ-ONLY (discover, validate, ERD) | All active agents |
| 3 | **Data Model Builder** | Active | Tables, fields, relationships, option sets, keys | WRITE (plan + execute schema ops) | Tester, Architect |
| 4 | **UI Builder** | Active | Forms, views, dashboards, charts, model-driven apps | WRITE (plan + execute UI ops) | Tester, Architect |
| 5 | **Data Population** | Active | Seed/reference data, sample test data | WRITE (create records) | Tester, Architect |
| 6 | **Tester** | Active | Post-execution validation, schema verification | READ-ONLY (verify, compare, query) | Architect |
| 7 | **Security Builder** | Active (Phase 6) | Security roles + privilege matrix (create / update delta / delete) | WRITE (plan + execute role ops) | Tester, Architect |
| 8 | **Automation Builder** | Active (Phase 7) — lifecycle only | Rename / activate / deactivate / delete BPFs + Business Rules. Creation of new workflows deferred until XAML templates can be validated against real Dataverse | WRITE (limited surface — no XAML generation) | Tester, Architect |
| 9 | **Documentor** | Roadmap (N/A) | Data dictionary, changelog, ERD, README, GitHub push | — | — |

> Reviewer was removed in commit `0337550` — its checks moved into each
> builder's plan_* tools, which catch the same class of issues earlier with
> fewer round-trips. Approval still gates every write through the user;
> only the separate pre-flight agent disappeared.

### Why these agents?

**Business Analyst** — In real D365 projects, a BA gathers and refines requirements BEFORE the Solution Architect designs anything. Without the BA, the user says "I need a feedback module" and the Architect immediately starts designing tables. With the BA, the user gets asked clarifying questions first: Who submits feedback? What's the review process? What rating scale? This prevents building the wrong schema.

The BA is ONLY involved for ambiguous/broad requests. Specific instructions ("add a phone number field to Contact") skip the BA and go directly to the Architect.

**Security Builder** (Phase 6) — Security is NOT an afterthought. Every enterprise project needs security roles with the right privilege matrix at the right depth. The agent owns role create / update / delete with a privilege-delta planner so updates don't blow away the live matrix and a safety pre-flight that refuses to delete a role anyone is still using. Field-level security profiles + column security stay deferred to v2.

**Data Population** — After creating a FeedbackCategory table, the next natural step is "populate it with categories." After creating the full module, the user needs test data. This agent handles both reference/seed data and realistic test records.

**Tester** — Critical for trust. We create things, but do they actually match the spec? The Tester re-reads from the API after every execution and compares actual vs planned. It also runs smoke tests: can the form load? Does the view return results? Is the role's privilege matrix exactly what the plan asked for? This is the **post-execution** quality gate.

**Roadmap — Automation Builder + Documentor.** Plugins / BPFs / business rules require code-gen + GitHub branch flow that we haven't designed yet; the current path is "create the artifacts manually in the maker portal and let the agents read them". Documentor is similar — the data dictionary + ERD work needs a writeable docs target which the platform doesn't yet expose.

---

## 3. Model Strategy Per Agent

### Model Assignment

Not every agent needs GPT-4o. The right model depends on reasoning complexity, output quality requirements, and task predictability.

| Agent | Model | Rationale |
|-------|-------|-----------|
| Solution Architect | Configurable | Complex orchestration, design reasoning, user conversation |
| Business Analyst | Configurable | Requirement elicitation requires strong conversational reasoning |
| Automation Builder | Configurable | C# plugin code generation, BPF design — code quality matters |
| Data Model Builder | **Configurable** | Translates clear specs into Dataverse API calls — structured work |
| UI Builder | **Configurable** | Form XML and FetchXML generation follows patterns |
| Security Builder | **Configurable** | Privilege matrix mapping — pattern-based |
| Data Population Agent | **Configurable** | Data generation — creative but not complex reasoning |
| Tester | **Configurable** | Comparison tasks (expected vs actual) — straightforward |
| Reviewer | **Configurable** | Best practice checking against known rule set (exception: plugin code review may use GPT-4o) |
| Documentor | **Configurable** | Template-based documentation |

### Cost Optimization: ~60% cheaper than all-GPT-4o

```
Optimized mix:  3 × GPT-4o + 7 × Configurable  = ~40% of all-GPT-4o baseline
For production usage (100 sessions/day): ~$900/month vs ~$2,250/month
```

---

## 4. Orchestration Design

### Handoff Topology & Rules

```
HANDOFF RULES:
─────────────
Business Analyst    → Solution Architect (always returns with structured spec)
Solution Architect  → ANY agent (full access, including BA for ambiguous requests)
Data Model Builder  → Reviewer, Tester, Solution Architect
UI Builder          → Reviewer, Tester, Solution Architect
Automation Builder  → Reviewer, Tester, Solution Architect
Security Builder    → Reviewer, Solution Architect
Data Population Agt → Tester, Solution Architect
Tester              → Solution Architect (always returns)
Reviewer            → Solution Architect (always returns)
Documentor          → Solution Architect (always returns)

CONSTRAINTS:
- NO builder can hand off directly to another builder
- All cross-builder coordination goes through the Architect
- BA only involved for ambiguous/broad requests (Architect decides)
- Tester, Reviewer, Documentor always return to Architect
- Documentor runs automatically after every successful build+test cycle
```

### Workflow Pattern: BA → Design → Review → Approve → Execute → Test → Document

```
User Request (ambiguous)
    │
    ▼
Solution Architect (evaluates ambiguity)
    │
    ├─► AMBIGUOUS → HANDOFF → Business Analyst
    │       │
    │       ├── BA asks clarifying questions (2-5 turns)
    │       ├── BA produces structured Requirement Spec
    │       ├── BA confirms with user
    │       └─► HANDOFF → Solution Architect (with requirements)
    │
    ├─► CLEAR → Architect proceeds directly
    │
    ▼
Solution Architect (design)
    │
    ├─► HANDOFF → Builder Agent (plan_*)
    │       │
    │       ├─► HANDOFF → Reviewer (validate plan)
    │       │       │
    │       │       └─► HANDOFF → Architect (present review to user)
    │       │               │
    │       │               ▼
    │       │         🛑 HUMAN APPROVAL
    │       │               │ (if approved)
    │       │               ▼
    │       │         HANDOFF → Builder Agent (execute_*)
    │       │               │
    │       │               ├─► HANDOFF → Tester (verify execution)
    │       │               │       │
    │       │               │       ├── ✅ All checks passed
    │       │               │       │       │
    │       │               │       │       └─► HANDOFF → Documentor
    │       │               │       │               │
    │       │               │       │               └─► HANDOFF → Architect
    │       │               │       │                       │
    │       │               │       │                       ▼
    │       │               │       │                 "Done! What's next?"
    │       │               │       │
    │       │               │       └── ❌ Verification failed
    │       │               │               │
    │       │               │               └─► HANDOFF → Architect
    │       │               │                       │
    │       │               │                       ▼
    │       │               │                 Report issue to user
    │       │               │                 Offer: retry, fix, skip
```

### Simplified Flow for Low-Risk Operations

Not every operation needs the full ceremony. For green/yellow risk:

```
User: "Add a description field to CustomerFeedback"

Solution Architect → Data Model Builder
    → plan_create_field (memo, richText)
    → 🟡 auto-proceed (just a field)
    → execute_create_field
    → Tester verifies field exists with correct type
    → Documentor updates data dictionary
    → Architect: "Done. Added cr_description as Rich Text."
```

### Risk-Based Approval Model

```
🟢 AUTO-APPROVE (no human needed)
   • Read operations (get_tables_list, get_environment_info)
   • Generating documentation, ERD diagrams
   • Validation checks
   • Creating views

🟡 NOTIFY + AUTO-PROCEED (user sees it, can cancel within 5s)
   • Creating a field on an existing table
   • Modifying a form (adding fields)
   • Creating a business rule
   • Populating seed data (< 20 records)
   • Pushing docs to GitHub

🟠 REQUIRE APPROVAL (user must click Approve)
   • Creating a new table
   • Creating a relationship
   • Creating a BPF
   • Creating/modifying a security role
   • Plugin code (shows code for review)
   • Creating a Custom API
   • Populating large datasets (> 20 records)

🔴 BLOCKED (dev/test environments only)
   • Deleting a table or field
   • Running raw scripts (dataverse_runScript)
   • Changing field types (destructive)
   • Any write on production environments
```

### Shared Project State

```csharp
public class ProjectState
{
    // ─── IDENTITY ───
    public string ProjectId { get; set; }
    public string ProjectName { get; set; }
    
    // ─── ONBOARDING ───
    public OnboardingStatus Onboarding { get; set; } = OnboardingStatus.NotStarted;
    
    // ─── ENVIRONMENT ───
    public string EnvironmentUrl { get; set; }
    public string EnvironmentName { get; set; }
    public int BaseLanguageLcid { get; set; }
    public string DataverseVersion { get; set; }
    public bool EnvironmentConfirmed { get; set; }
    
    // ─── SOLUTION ───
    public string SolutionUniqueName { get; set; }
    public string SolutionDisplayName { get; set; }
    public string SolutionVersion { get; set; }
    public Guid SolutionId { get; set; }
    public bool SolutionConfirmed { get; set; }
    
    // ─── PUBLISHER ───
    public string PublisherDisplayName { get; set; }
    public string PublisherUniqueName { get; set; }
    public string PublisherPrefix { get; set; }
    public int PublisherOptionValuePrefix { get; set; }
    public Guid PublisherId { get; set; }
    
    // ─── CREATED ARTIFACTS ───
    public List<CreatedTable> Tables { get; set; } = [];
    public List<CreatedField> Fields { get; set; } = [];
    public List<CreatedRelationship> Relationships { get; set; } = [];
    public List<CreatedOptionSet> OptionSets { get; set; } = [];
    public List<CreatedForm> Forms { get; set; } = [];
    public List<CreatedView> Views { get; set; } = [];
    public List<CreatedPlugin> Plugins { get; set; } = [];
    public List<CreatedBpf> BusinessProcessFlows { get; set; } = [];
    public List<CreatedBusinessRule> BusinessRules { get; set; } = [];
    public List<CreatedSecurityRole> SecurityRoles { get; set; } = [];
    public List<CreatedRecord> SeedDataRecords { get; set; } = [];
    
    // ─── OPERATION HISTORY (for undo) ───
    public Stack<ExecutedOperation> OperationStack { get; set; } = new();
    
    // ─── PENDING ───
    public List<ProposedChange> PendingChanges { get; set; } = [];
    
    // ─── AUDIT ───
    public List<AuditEntry> AuditLog { get; set; } = [];
    
    // ─── SESSION ───
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public string ConversationSummary { get; set; }
    
    // ─── CONVENIENCE ───
    public bool IsReady => EnvironmentConfirmed && SolutionConfirmed;
    public string FullPrefix => $"{PublisherPrefix}_";
}

public enum OnboardingStatus
{
    NotStarted,
    EnvironmentConfirmed,
    SolutionSelected,
    SolutionCreated,
    Ready
}
```

---

## 5. Solution Onboarding Workflow

**Rule: No write operation can happen without a confirmed solution context. Enforced at prompt, middleware, and state levels.**

### Flow

```
Step 1: ENVIRONMENT CONFIRMATION
─────────────────────────────────
→ get_environment_info
→ Present: name, URL, base language, version
→ User confirms or switches
→ EnvironmentConfirmed = true

Step 2: SOLUTION DISCOVERY
──────────────────────────
→ get_solutions_list (unmanaged, exclude system)
→ get_publishers
→ Present solutions as selectable cards with:
   • Display name + unique name
   • Publisher + prefix
   • Component count
   • Last modified date
→ Offer: [Select existing] or [Create new]

Step 3A: USE EXISTING SOLUTION
──────────────────────────────
→ get_solution_components (grouped by type)
→ Show what's already inside
→ Confirm publisher prefix for new components
→ User confirms
→ SolutionConfirmed = true

Step 3B: CREATE NEW SOLUTION
────────────────────────────
→ Select or create publisher
   If new: ask name, prefix (2-8 lowercase), option value prefix (10000-99999)
   → plan_create_publisher → human confirms → execute_create_publisher
→ Suggest solution details (name, unique name, version, description)
→ User reviews/modifies
→ plan_create_solution → human confirms → execute_create_solution
→ SolutionConfirmed = true

GUARD: If user asks to create anything before onboarding:
"Before I can create anything, I need to know which solution to work in."
→ Start onboarding flow

GUARD: Default Solution is FORBIDDEN. It's a Dataverse anti-pattern.
```

---

## 6. Complete Tool Catalog

### Legend

| Symbol | Meaning |
|--------|---------|
| 📖 | Read-only (no side effects) |
| 📋 | Plan (generates spec, no side effects) |
| ✏️ | Write (modifies Dataverse) |
| ✅ | Verify (reads and compares) |

### 6.1 Solution Architect — Read-Only Tools

```
📖 get_environment_info
   → organizationName, baseLanguage (LCID), version, region, currency,
     fiscalYearStart, isAuditEnabled, maxUploadFileSize
   API: GET /api/data/v9.2/organizations

📖 get_solutions_list
   → solutions[]: uniqueName, displayName, version, publisher, prefix,
     optionValuePrefix, isManaged, componentCount, modifiedOn
   Params: filter (all/unmanaged/managed), excludeDefault, excludeSystem
   API: GET /api/data/v9.2/solutions?$filter=ismanaged eq false&$expand=publisherid

📖 get_solution_components
   → Grouped by type: tables[], fields[], relationships[], forms[], views[],
     plugins[], workflows[], webResources[], optionSets[], securityRoles[]
   → summary: { totalComponents, byType }
   API: GET /api/data/v9.2/solutioncomponents + metadata lookups

📖 get_publishers
   → publishers[]: displayName, uniqueName, prefix, optionValuePrefix, publisherId
   API: GET /api/data/v9.2/publishers?$filter=isreadonly eq false

📖 get_tables_list
   → tables[]: logicalName, displayName, schemaName, isCustom, isManaged,
     ownershipType, primaryIdAttribute, primaryNameAttribute, hasNotes,
     hasActivities, changeTrackingEnabled, rowCount (approx)
   Params: filter (all/custom/oob/solution), solutionName, searchTerm

📖 get_table_details
   → table metadata + all columns[] with full type info + relationships[] + keys[]
   API: GET /api/data/v9.2/EntityDefinitions(LogicalName='{name}')?$expand=Attributes,Keys

📖 get_table_relationships
   → oneToMany[], manyToOne[], manyToMany[] with cascade behaviors
   Params: direction (all/incoming/outgoing)

📖 get_global_option_sets
   → optionSets[]: name, displayName, isGlobal, options[]: { value, label, color }

📖 get_existing_fields_for_reuse
   → matches across tables for a given field name pattern / type
   Purpose: avoid duplicating fields when OOB ones exist

📖 validate_schema_name
   → isValid, validatedName (with prefix), issues[], suggestion
   Rules: max length, no spaces, no hyphens, lowercase, prefix applied, not reserved

📖 check_oob_table_coverage
   → recommendations[]: oobTableName, matchConfidence, explanation, missingFields
   Purpose: prevent creating custom tables when OOB tables suffice

📖 analyze_data_model_impact
   → impactedForms[], impactedViews[], impactedPlugins[], impactedFlows[],
     riskLevel, warnings[]
   Purpose: assess impact before proposing changes

📖 generate_erd_mermaid
   → mermaidCode, tableCount, relationshipCount
   Params: tables[], includeOobRelationships, includeFields, includeSystemFields

📋 plan_create_publisher
   → spec, warnings (prefix uniqueness, format validation)
   
✏️ execute_create_publisher
   API: POST /api/data/v9.2/publishers

📋 plan_create_solution
   → spec, publisherDetails, warnings (unique name collision)

✏️ execute_create_solution
   API: POST /api/data/v9.2/solutions
```

### 6.2 Data Model Builder — Schema Tools

```
── TABLE OPERATIONS ──

📋 plan_create_table
   Params: tableName, displayName, pluralDisplayName, description,
     ownershipType (UserOwned/OrganizationOwned), primaryFieldName,
     primaryFieldDisplayName, primaryFieldMaxLength, hasNotes, hasActivities,
     changeTrackingEnabled (default true), isAuditEnabled (default true)
   Returns: spec with webApiPayload, warnings, riskLevel: 🟠

✏️ execute_create_table
   API: POST /api/data/v9.2/EntityDefinitions + AddSolutionComponent
   Updates: ProjectState.Tables, OperationStack

📋 plan_modify_table
   Params: tableLogicalName, changes (displayName, description, hasNotes, 
     hasActivities, changeTrackingEnabled, isAuditEnabled)
   Returns: spec, diff { before, after }

✏️ execute_modify_table
   API: PUT /api/data/v9.2/EntityDefinitions({metadataId})


── FIELD OPERATIONS ──

📋 plan_create_field
   Params: tableLogicalName, fieldName, displayName, description,
     fieldType, fieldTypeConfig (JSON), requiredLevel, isSearchable, isAuditEnabled
   Returns: spec with webApiPayload, warnings, riskLevel: 🟡

   SUPPORTED FIELD TYPES:

   "string"
     → maxLength (1-4000, default 100)
     → format: text | email | url | phone | tickerSymbol | textArea

   "memo" (multiline text)
     → maxLength (1-1048576, default 2000)
     → format: text | richText | email

   "integer"
     → minValue, maxValue
     → format: none | duration | timeZone | language | locale

   "decimal"
     → minValue, maxValue, precision (0-10, default 2)

   "float"
     → minValue, maxValue, precision (0-5, default 2)

   "money" (currency)
     → minValue, maxValue, precision (0-4, default 2)
     → precisionSource: currency | field

   "boolean" (two option)
     → trueLabel (default "Yes"), falseLabel (default "No"), defaultValue

   "dateTime"
     → format: dateOnly | dateAndTime
     → behavior: userLocal | dateOnly | timeZoneIndependent

   "localOptionSet" (choice)
     → options[]: { label, value?, description?, color? }
     → defaultValue?

   "localMultiSelectOptionSet" (choices)
     → options[]: same as above
     → defaultValues[]?

   "globalOptionSet" (reference existing)
     → globalOptionSetName

   "lookup"
     → targetTable
     → relationshipName?
     → cascadeAssign, cascadeDelete, cascadeReparent, cascadeMerge, 
       cascadeShare, cascadeUnshare
     → lookupBehavior: referential | parental | referentialRestrictDelete

   "customer" (polymorphic → Account + Contact)
     → no additional config

   "file"
     → maxSizeInKB (default 32768), allowedExtensions[]?

   "image"
     → maxSizeInKB (default 10240), canStoreFullImage, isPrimaryImage

   "calculated"
     → resultType (string/integer/decimal/money/dateTime/boolean)
     → formula (Power Fx)

   "rollup"
     → resultType, relatedTable, relatedField
     → aggregation: sum | count | min | max | avg
     → filter? (fetchXml)

   "autoNumber"
     → format: e.g. "FB-{SEQNUM:5}" or "{DATETIMEUTC:yyyyMMdd}-{SEQNUM:4}"
     → maxLength

✏️ execute_create_field
   API: POST /api/data/v9.2/EntityDefinitions({tableId})/Attributes
   Updates: ProjectState.Fields, OperationStack

📋 plan_modify_field
   → Only modifiable properties: displayName, description, requiredLevel,
     isSearchable, isAuditEnabled, maxLength (increase only)
   Returns: spec, diff, warnings

✏️ execute_modify_field
   API: PUT /api/data/v9.2/EntityDefinitions({tableId})/Attributes({fieldId})


── RELATIONSHIP OPERATIONS ──

📋 plan_create_one_to_many_relationship
   Params: primaryTable, relatedTable, lookupFieldName, lookupFieldDisplayName,
     relationshipSchemaName?, cascadeBehavior (all 6 types), lookupBehavior
   Returns: spec, erDiagramUpdate (mermaid fragment), riskLevel: 🟠

✏️ execute_create_one_to_many_relationship
   API: POST /api/data/v9.2/RelationshipDefinitions

📋 plan_create_many_to_many_relationship
   Params: firstTable, secondTable, relationshipSchemaName?, intersectTableName?
   Returns: spec, intersectTableName, erDiagramUpdate

✏️ execute_create_many_to_many_relationship


── OPTION SET OPERATIONS ──

📋 plan_create_global_option_set
   Params: name, displayName, description, optionSetType, options[]
   Returns: spec, riskLevel: 🟡

✏️ execute_create_global_option_set
   API: POST /api/data/v9.2/GlobalOptionSetDefinitions

📋 plan_add_option_to_option_set
   Params: optionSetName (or table+field for local), isGlobal, newOption
   Returns: spec

✏️ execute_add_option_to_option_set
   API: POST /api/data/v9.2/InsertOptionValue


── ALTERNATE KEY OPERATIONS ──

📋 plan_create_alternate_key
   Params: tableLogicalName, keyName, displayName, keyAttributes[] (max 5)
   Returns: spec, warnings

✏️ execute_create_alternate_key
   API: POST /api/data/v9.2/EntityDefinitions({id})/Keys
```

### 6.3 UI Builder — Form, View, Dashboard, App Tools

```
── FORM OPERATIONS ──

📖 get_forms_list
   Params: tableLogicalName, formType (main/quickCreate/quickView/card/all)
   Returns: forms[]: formId, name, type, isDefault, isManaged

📖 get_form_definition
   Params: tableLogicalName, formId
   Returns: formXml, tabs[]: { name, sections[]: { fields[] } }, headerFields, businessRules

📋 plan_create_form
   Params: tableLogicalName, formName, formType (main/quickCreate), description,
     layout: {
       headerFields[],
       tabs[]: {
         label, columns (1|2),
         sections[]: {
           label,
           fields[]: { fieldLogicalName, isVisible, isReadOnly, isRequired, labelOverride }
         }
       },
       subgrids[]: { relatedTable, relationshipName, viewId? },
       timeline: bool
     }
   Returns: spec, formXml, riskLevel: 🟡

✏️ execute_create_form
   API: POST /api/data/v9.2/systemforms

📋 plan_modify_form
   Params: formId, operations[]:
     addField | removeField | moveField | addTab | removeTab |
     addSection | removeSection | addSubgrid | setVisibility | setRequired
   Returns: spec, diff { before, after }

✏️ execute_modify_form
   API: PUT /api/data/v9.2/systemforms({formId})


── VIEW OPERATIONS ──

📖 get_views_list
   Params: tableLogicalName, viewType (system/personal/all)

📋 plan_create_view
   Params: tableLogicalName, viewName, description,
     columns[]: { fieldLogicalName, width, label? },
     filter: {
       conditions[]: { field, operator, value? },
       filterType (and/or),
       linkedEntityFilters[]?
     },
     sortOrder: { field, direction }
   Returns: spec, fetchXml, layoutXml, riskLevel: 🟢

✏️ execute_create_view
   API: POST /api/data/v9.2/savedqueries


── DASHBOARD OPERATIONS ──

📋 plan_create_dashboard
   Params: dashboardName, description, layout, 
     components[]: { type (chart/view/iframe/webResource), position, config }
   Returns: spec, formXml

✏️ execute_create_dashboard
   API: POST /api/data/v9.2/systemforms (type=dashboard)


── MODEL-DRIVEN APP OPERATIONS ──

📋 plan_create_model_driven_app
   Params: appName, appUniqueName, description,
     siteMap: { areas[]: { title, groups[]: { title, subAreas[] } } },
     includedTables[], dashboards[]?
   Returns: spec, siteMapXml

✏️ execute_create_model_driven_app
   API: POST /api/data/v9.2/appmodules + sitemap
```

### 6.4 Automation Builder — Logic & Process Tools

```
── PLUGIN OPERATIONS ──

📋 plan_create_plugin
   Params: tableLogicalName, pluginName, message, stage, executionMode,
     filteringAttributes[]?, businessLogic (natural language),
     preImage, preImageAttributes[]?, postImage, postImageAttributes[]?,
     executionOrder, description
   Returns: spec, csharpCode, registrationSteps, nugetReferences,
     projectStructure, unitTestCode, riskLevel: 🟠

📋 plan_create_plugin_project
   Params: projectName, solutionName, targetFramework, plugins[], 
     includeUnitTests, includeEarlyBound
   Returns: files[], csprojContent, buildInstructions

✏️ execute_push_to_github
   Params: repositoryUrl, branchName, files[], commitMessage,
     createPullRequest, prTitle?, prDescription?
   Returns: branchUrl, pullRequestUrl?, commitSha


── CUSTOM API OPERATIONS ──

📋 plan_create_custom_api
   Params: uniqueName, displayName, description, bindingType, 
     boundEntityLogicalName?, isFunction, isPrivate,
     requestParameters[], responseProperties[], pluginType?
   Returns: spec, pluginCode?, webApiUsage, riskLevel: 🟠

✏️ execute_create_custom_api
   API: POST /api/data/v9.2/customapis + parameters + response properties


── BUSINESS PROCESS FLOW OPERATIONS ──

📋 plan_create_bpf
   Params: bpfName, displayName, description, primaryTable,
     stages[]: {
       name, displayName, category,
       entity? (for cross-entity),
       stageSteps[]: { fieldLogicalName, isRequired, sequenceNumber },
       branchingCondition?: { field, operator, value, targetStage }
     },
     crossEntityNavigation[]?: { fromStage, toStage, relationshipName }
   Returns: spec, stageFlowDiagram, warnings, riskLevel: 🟠

✏️ execute_create_bpf
   API: POST /api/data/v9.2/workflows (category=BPF) + stages + steps


── BUSINESS RULE OPERATIONS ──

📋 plan_create_business_rule
   Params: tableLogicalName, ruleName, description, scope,
     conditions[]: { field, operator, value, logicalOperator },
     actions[]: { type (setValue/setRequired/setVisibility/showError/
       setDefault/lockField/showSection/hideTab), targetField, value? },
     elseActions[]?
   Returns: spec, ruleDefinition, riskLevel: 🟡

✏️ execute_create_business_rule
   API: POST /api/data/v9.2/workflows (category=BusinessRule)


── WEBHOOK OPERATIONS ──

📋 plan_create_webhook
   Params: name, endpointUrl, authType, message, tableLogicalName,
     stage, executionMode, filteringAttributes[]?
   Returns: spec, registrationSteps

✏️ execute_register_webhook
   API: POST /api/data/v9.2/serviceendpoints + step registration


── ESCAPE HATCH ──

✏️ dataverse_run_script
   Params: scriptBody (C#), description
   Returns: success, result, message
   Risk: 🔴 BLOCKED by default. Requires explicit user override.
```

### 6.5 Security Builder — Security Tools (NEW)

```
── SECURITY ROLE OPERATIONS ──

📖 get_security_roles
   Params: filter (all/custom/system)
   Returns: roles[]: roleId, name, isManaged, isCustomizable, 
     businessUnitId, privilegeCount
   API: GET /api/data/v9.2/roles

📖 get_security_role_details
   Params: roleId
   Returns: privileges[]: { entityName, privilegeName (Create/Read/Write/Delete/
     Append/AppendTo/Assign/Share), depth (User/BU/ParentChild/Org) }
   API: GET /api/data/v9.2/roles({id})/roleprivileges_association

📋 plan_create_security_role
   Params: roleName, description, 
     copyFromRole? (clone existing as starting point),
     privileges[]: {
       tableName, 
       create: depth?, read: depth?, write: depth?, delete: depth?,
       append: depth?, appendTo: depth?, assign: depth?, share: depth?
     }
   Returns: spec, privilegeMatrix (visual table), warnings, riskLevel: 🟠
   
   Depth values: none | user | businessUnit | parentChildBU | organization

✏️ execute_create_security_role
   API: POST /api/data/v9.2/roles + privilege assignment

📋 plan_modify_security_role
   Params: roleId, changes (add/remove/change privileges)
   Returns: spec, diff, riskLevel: 🟠

✏️ execute_modify_security_role


── FIELD-LEVEL SECURITY ──

📖 get_field_security_profiles
   Returns: profiles[]: profileId, name, fieldPermissions[]

📋 plan_create_field_security_profile
   Params: profileName, description,
     fieldPermissions[]: {
       tableLogicalName, fieldLogicalName,
       canRead: bool, canUpdate: bool, canCreate: bool
     }
   Returns: spec, sensitiveFieldsDetected[], riskLevel: 🟠

✏️ execute_create_field_security_profile
   API: POST /api/data/v9.2/fieldsecurityprofiles + fieldpermissions

📋 plan_enable_field_security
   Params: tableLogicalName, fieldLogicalName
   Purpose: enable IsSecured on a field so it CAN be protected by profiles
   Returns: spec, warning ("once enabled, cannot be disabled")

✏️ execute_enable_field_security


── SECURITY RECOMMENDATIONS ──

📖 analyze_security_gaps
   Purpose: scan created tables/fields and identify security gaps
   Returns:
     - piiFieldsWithoutSecurity[]: fields that LOOK like PII but lack field-level security
       (detects: email, phone, SSN, salary, address, birthdate, medical, financial patterns)
     - tablesWithoutCustomRoles[]: custom tables that only have default role coverage
     - overPrivilegedRoles[]: roles with Org-level delete on sensitive tables
     - recommendations[]: actionable suggestions
```

### 6.6 Data Population Agent — Data Tools (NEW)

```
── SEED / REFERENCE DATA ──

📋 plan_populate_reference_data
   Params: tableLogicalName, 
     records[]: { fieldValues: { fieldName: value, ... } }
   Returns: spec, recordCount, warnings (missing required fields, 
     invalid option set values, nonexistent lookups), riskLevel: 🟡

✏️ execute_populate_reference_data
   API: POST /api/data/v9.2/{entitySetName} (batch request for multiple records)
   Uses: $batch endpoint for efficiency (up to 1000 per batch)

📋 plan_generate_reference_data
   Purpose: Given a table definition, SUGGEST appropriate seed data
   Params: tableLogicalName, recordCount (default: 5-10)
   Returns: suggestedRecords[] with realistic values
   Example: FeedbackCategory → ["Product Quality", "Customer Service", 
     "Delivery", "Pricing", "User Experience", "Other"]


── SAMPLE / TEST DATA ──

📋 plan_generate_test_data
   Purpose: Generate realistic test records for the main entity
   Params: tableLogicalName, recordCount (default: 10),
     includeRelatedRecords (default: true),
     locale (default: from environment base language)
   Returns: records[] with:
     - Realistic names, dates, descriptions
     - Valid option set values
     - Valid lookup references (to existing or seed data)
     - Variety across choice fields
   Example: CustomerFeedback → 10 records with different ratings, categories,
     dates, descriptions, contacts, priorities

✏️ execute_generate_test_data
   API: $batch POST operations
   Risk: 🟡 (<20 records), 🟠 (>20 records)


── DATA MANAGEMENT ──

📖 count_records
   Params: tableLogicalName, filter? (fetchXml)
   Returns: count
   API: GET /api/data/v9.2/{entitySet}?$count=true

📋 plan_delete_test_data
   Purpose: Clean up test data created by this agent
   Params: tableLogicalName, filter (only records tagged as test data)
   Returns: recordsToDelete, riskLevel: 🟠
   Note: Only deletes records that were created by the Data Population Agent
     (tracked via a custom tag field or Project State)

✏️ execute_delete_test_data
   API: $batch DELETE operations
```

### 6.7 Tester — Post-Execution Validation Tools (NEW)

```
── SCHEMA VERIFICATION ──

✅ verify_table_creation
   Purpose: After execute_create_table, re-read and compare
   Params: expectedSpec (from plan_create_table), tableLogicalName
   Returns:
     - exists: bool
     - matches: bool
     - discrepancies[]: { property, expected, actual }
     - verdict: PASS | FAIL | PARTIAL
   API: GET /api/data/v9.2/EntityDefinitions(LogicalName='{name}')

✅ verify_field_creation
   Purpose: After execute_create_field, verify type, constraints, etc.
   Params: expectedSpec, tableLogicalName, fieldLogicalName
   Returns:
     - exists: bool
     - typeMatches: bool
     - constraintsMatch: bool (maxLength, required, searchable, etc.)
     - discrepancies[]
     - verdict: PASS | FAIL
   API: GET /api/data/v9.2/EntityDefinitions({id})/Attributes(LogicalName='{name}')

✅ verify_relationship_creation
   Params: expectedSpec, relationshipSchemaName
   Returns: exists, cascadeBehaviorMatches, discrepancies[], verdict

✅ verify_option_set
   Params: expectedSpec, optionSetName, isGlobal
   Returns: exists, optionsMatch (labels, values, colors), discrepancies[], verdict


── FORM & VIEW VERIFICATION ──

✅ verify_form_creation
   Purpose: After creating a form, verify it's valid
   Params: tableLogicalName, formId
   Returns:
     - exists: bool
     - allFieldsExist: bool — every field referenced in FormXML exists on the table
     - missingFields[]: fields in the form that don't exist on the entity
     - allSubgridsValid: bool — all subgrid relationships are valid
     - invalidSubgrids[]: broken references
     - verdict: PASS | FAIL | WARNINGS

✅ verify_view_creation
   Purpose: After creating a view, verify it works
   Params: tableLogicalName, viewId
   Returns:
     - exists: bool
     - fetchXmlValid: bool — FetchXML parses and executes without error
     - allColumnsExist: bool — all layout columns exist on the entity
     - missingColumns[]
     - queryReturnsResults: bool (runs the query with top 1)
     - verdict: PASS | FAIL | WARNINGS


── BPF VERIFICATION ──

✅ verify_bpf_creation
   Params: bpfLogicalName
   Returns:
     - exists: bool
     - isActivated: bool
     - allStageFieldsExist: bool — every step field exists on the stage entity
     - missingStageFields[]
     - crossEntityLookupsValid: bool (for cross-entity BPFs)
     - verdict: PASS | FAIL | WARNINGS


── SECURITY VERIFICATION ──

✅ verify_security_role
   Params: expectedSpec, roleId
   Returns:
     - exists: bool
     - privilegesMatch: bool
     - discrepancies[]: { entity, privilege, expectedDepth, actualDepth }
     - verdict: PASS | FAIL


── COMPREHENSIVE VALIDATION ──

✅ run_full_validation
   Purpose: After a complete build cycle, validate EVERYTHING
   Params: none (reads from ProjectState)
   Returns:
     - tables: { total, passed, failed, details[] }
     - fields: { total, passed, failed, details[] }
     - relationships: { total, passed, failed, details[] }
     - forms: { total, passed, failed, details[] }
     - views: { total, passed, failed, details[] }
     - bpfs: { total, passed, failed, details[] }
     - securityRoles: { total, passed, failed, details[] }
     - overallVerdict: PASS | PARTIAL | FAIL
     - summary: "22/24 components verified. 2 warnings."
```

### 6.8 Reviewer — Pre-Execution Review Tools

```
📖 validate_schema_design
   Params: proposedSpec, validationRules (naming/relationships/performance/
     security/limits/bestPractice/all)
   Returns: isValid, score (0-100), issues[] (severity/rule/message/suggestion),
     recommendations[]

   Rules checked:
     Naming: prefix, casing, length, reserved words
     Relationships: cascade delete safety, circular detection
     Performance: rollup on high-volume tables, view join count
     Limits: field count, table count, option set options
     Best practice: missing descriptions, audit not enabled,
       search not enabled on important fields, missing alternate keys

📖 validate_plugin_code
   Params: csharpCode, pluginSpec
   Returns: isValid, issues[] (severity/line/message/suggestion)

   Rules checked:
     ✅ Uses ITracingService
     ✅ Validates InputParameters
     ✅ Proper null checks
     ✅ InvalidPluginExecutionException for business errors
     ✅ No async/await in sync plugins
     ✅ No HttpClient in sync plugins
     ✅ No Thread.Sleep
     ✅ No swallowed exceptions
     ✅ Filtering attributes set (for Update)
     ✅ Idempotent
     ⚠️ Complex logic that should be split
     ⚠️ Missing unit tests

📖 check_security_implications
   Returns: securityIssues[], affectedRoles[], fieldLevelSecurityNeeded,
     auditRecommendations[]

📖 estimate_performance_impact
   Params: proposedChanges, currentTableSize?
   Returns: performanceRisk, concerns[], recommendations[]
```

### 6.9 Documentor — Documentation Tools

```
📋 generate_data_dictionary
   Params: tables[] (or all from ProjectState), format (markdown/html/csv),
     includeSystemFields, includeRelationships, includeOptionSetValues
   Returns: content, filename

📋 generate_changelog_entry
   Params: changes[] (from ProjectState.OperationStack), version?
   Returns: changelogEntry (markdown)

📋 generate_erd_mermaid
   → mermaidCode from current project state

📋 generate_technical_documentation
   Params: includeERD, includePluginDocs, includeSecurityMatrix, includeDeploymentGuide
   Returns: readmeContent, architectureDiagram, securityMatrix, deploymentGuide

✏️ push_documentation_to_github
   Params: repositoryUrl, branchName, files[], commitMessage
   Returns: commitUrl
```

---

## 7. Agent System Prompts

### 7.1 Solution Architect

```
You are the Solution Architect for Dataverse AI Forge, a multi-agent system that helps 
professionals build Dataverse solutions through conversation.

═══════════════════════════════════
 YOUR ROLE
═══════════════════════════════════

You are the planner, coordinator, and primary conversational partner. You are the 
ONLY agent the user talks to directly. Other agents work behind the scenes.

Think of yourself as a senior D365 Solution Architect / FastTrack Architect who:
- Listens to business requirements and translates them into technical designs
- Knows Dataverse inside-out
- Makes opinionated recommendations based on Microsoft best practices
- Coordinates a team of 8 specialists
- Never executes changes directly — always delegates

═══════════════════════════════════
 MANDATORY ONBOARDING
═══════════════════════════════════

Before doing ANY work, you MUST establish the solution context:

1. ENVIRONMENT: Call get_environment_info. Present name, URL, base language, version.
   Ask user to confirm. Do NOT proceed without confirmation.

2. SOLUTION: Call get_solutions_list (unmanaged, exclude system).
   Present available solutions with publisher, prefix, component count, last modified.
   Offer to create new. Let user choose.

3a. EXISTING: Call get_solution_components. Show contents. Confirm prefix. Wait for "yes".
3b. NEW: Select/create publisher → define solution → create. Confirm with user.

GUARDS:
- If user asks to create ANYTHING before onboarding: redirect to onboarding.
- NEVER use the Default Solution. It is a Dataverse anti-pattern.
- NEVER assume a prefix. Always confirm from the solution's publisher.

═══════════════════════════════════
 YOUR TEAM (8 AGENTS)
═══════════════════════════════════

• Data Model Builder — tables, fields, relationships, option sets, keys
  → Hand off when: schema changes are approved and ready for execution.

• UI Builder — forms, views, dashboards, model-driven apps, sitemaps
  → Hand off when: UI components need to be created or modified.

• Automation Builder — plugins, BPFs, business rules, custom APIs, webhooks, 
  flow specs. Also handles GitHub operations for code.
  → Hand off when: business logic or process automation is needed.

• Security Builder — security roles, field-level security profiles, column security.
  → Hand off when: a security model needs to be designed or applied.

• Data Population Agent — seed/reference data, sample test data.
  → Hand off when: tables need to be populated with initial or test data.

• Tester — post-execution validation. Verifies that what was created matches the spec.
  → Hand off when: a builder finishes executing. ALWAYS hand off to Tester after execution.

• Reviewer — pre-execution validation. Reviews proposals before they run.
  → Hand off when: a design or plan needs validation before presenting to user.

• Documentor — data dictionary, changelog, ERD, README, GitHub push.
  → Hand off AUTOMATICALLY after every successful build+test cycle.

═══════════════════════════════════
 HANDOFF RULES
═══════════════════════════════════

- You can hand off to ANY agent
- No builder can hand off to another builder (all cross-builder coordination through you)
- ALWAYS hand off to Reviewer before presenting critical designs to the user
- ALWAYS hand off to Tester after a builder executes changes
- ALWAYS hand off to Documentor after a successful test cycle
- Security Builder should be involved when creating tables with sensitive data

Standard sequence: Builder(plan) → Reviewer → User Approval → Builder(execute) → Tester → Documentor

═══════════════════════════════════
 WORKFLOW
═══════════════════════════════════

1. UNDERSTAND — Ask clarifying questions if ambiguous. One question at a time.
2. DISCOVER — Use read-only tools to understand current state.
3. DESIGN — Propose solution with specific names, types, relationships. Show ERD for multi-table.
4. VALIDATE — Hand off to Reviewer for independent validation.
5. PRESENT — Show design + review to user. Wait for approval.
6. DELEGATE — Hand off to builder with precise spec.
7. VERIFY — Hand off to Tester after execution.
8. DOCUMENT — Hand off to Documentor.
9. SECURE — If new tables were created, hand off to Security Builder to set up roles.
10. POPULATE — If reference tables were created, offer to populate them.
11. SUGGEST — Proactively suggest 2-3 logical next steps.

═══════════════════════════════════
 DATAVERSE DOMAIN KNOWLEDGE
═══════════════════════════════════

### Tables — Design Principles
- ALWAYS check OOB entities first: Account, Contact, Lead, Opportunity, Case, 
  Activity, Task, Email, PhoneCall, Note, Queue, Product, PriceList, Quote, 
  Order, Invoice, Knowledge Article, SLA, Entitlement.
- Don't create "Company" when Account exists. Don't create "Person" when Contact exists.
- Don't create "Ticket" when Case exists. Don't create "Deal" when Opportunity exists.
- UserOwned for most business data (enables sharing, assignment, team visibility).
- OrganizationOwned only for reference/config data (no ownership overhead).
- Enable change tracking on ALL custom tables (needed for sync, Data Export).
- Enable audit on tables with financial, medical, or personal data.
- If a table grows past ~40 fields, recommend splitting (main + detail with 1:1).

### Fields — Type Selection Intelligence
- Ratings → Choice (not Integer). Enables colored badges in views, structured values.
- Email → String with Email format. Enables click-to-email in forms.
- Phone → String with Phone format. Enables click-to-call.
- URL → String with URL format. Enables click-to-open.
- Money/Price → Currency type (not Decimal). Handles multi-currency, exchange rates.
- Status tracking → Status/StatusReason or Choice with colors.
- Categories → Lookup to reference table if values change often or need extra metadata.
  Choice if values are fixed and simple (< 20 options).
- Dates → DateOnly for birthdates/effective dates. DateAndTime for timestamps/events.
- Long text → Memo with RichText for user-facing content. Text format for internal/notes.
- Auto IDs → AutoNumber: "FB-{DATETIMEUTC:yyyyMMdd}-{SEQNUM:4}" for meaningful IDs.
- Yes/No → Boolean. Don't use integer 0/1 for flags.
- Percentage → Decimal (0-100) or Float. Not Integer (loses precision).

### Relationships — Rules
- 1:N is the default. Most relationships are parent-child.
- N:N only when BOTH sides are truly independent AND no data on the intersection.
  If you need fields on the intersection, use a junction table with two 1:N lookups.
- Referential behavior by default (soft reference).
- Parental ONLY for true parent-child where child can't exist without parent.
- Cascade Delete = RemoveLink is safest. Restrict for critical references.
  Cascade only for true child records that MUST be deleted with parent.
- NEVER Cascade Delete on tables that could have millions of child records.
  (A cascade delete of 10,000 records = timeout.)

### Naming Conventions
- Tables: PascalCase, singular (CustomerFeedback, not customer_feedbacks)
- Fields: PascalCase, descriptive (FeedbackDate, not fd or date1)
- Always include publisher prefix: {prefix}_{PascalCase}
- Max lengths: table logical name 128, field logical name 64
- No spaces, no hyphens, no special characters (only letters, numbers, underscore)

### Dataverse Limits to Know
- Max 1024 custom columns per table
- Max ~5000 custom tables per environment  
- Max 500 relationships per table
- Max 200 options per option set
- Max 5 columns in an alternate key
- Max 128 chars for table logical name, 64 for field logical name
- File field max: 128MB (configurable)
- API limits: 6000 requests per 5 minutes per user
- Calculated fields: evaluated at read time, no external calls
- Rollup fields: refresh every hour (or on-demand), only 1:N relationships
- BPF: max 5 entities, max 30 stages, each entity appears once

### Anti-Patterns to Catch
- Creating custom table when OOB exists (Company→Account, Person→Contact, Ticket→Case)
- Using text fields for structured data (dates in strings, numbers in strings)
- Too many fields on one table (>40 → suggest splitting)
- N:N when intersection data is needed (use junction table instead)
- Cascade Delete on large parent tables
- JavaScript for logic that should be a plugin (JS = client only, plugin = server + client)
- Hardcoded values → suggest environment variables
- Missing audit on financial/medical/personal data
- All fields set to "required" → suggest "recommended" for nice-to-have data
- No alternate keys on tables used for data import/integration

### Proactive Suggestions
After creating a table, ALWAYS suggest:
- "Want me to create a main form with tabs?"
- "I should set up some default views: Active, My, All."
- "Do you need a Business Process Flow for this?"
- "Should I set up security roles for who can access this data?"
- "I see you created a FeedbackCategory table — want me to populate it with initial categories?"

═══════════════════════════════════
 COMMUNICATION STYLE
═══════════════════════════════════

- Conversational but efficient. No lectures.
- ONE clarifying question at a time.
- Structured format for designs: table + fields + ERD.
- After completing a task, SUGGEST 2-3 next steps.
- Be honest about limitations.
- Match the user's vocabulary (table/column/field — don't correct terminology).
- When showing ERDs, use Mermaid syntax.
- When multiple options exist, present them with pros/cons/recommendation.

═══════════════════════════════════
 PROJECT STATE
═══════════════════════════════════

[INJECTED BY MIDDLEWARE AT RUNTIME]
```

### 7.2 Data Model Builder

```
You are the Data Model Builder for Dataverse AI Forge.

ROLE: Schema specialist. You create and modify tables, fields, relationships, 
option sets, and alternate keys. You receive specs from the Solution Architect 
and execute precisely.

BOUNDARIES:
✅ Tables, fields, relationships, option sets, keys
❌ Forms, views, dashboards (→ UI Builder)
❌ Plugins, BPFs, business rules (→ Automation Builder)
❌ Security roles (→ Security Builder)
❌ Data records (→ Data Population Agent)
❌ Delete operations (blocked by policy)
❌ Raw scripts
❌ Direct user communication

EXECUTION PATTERN:
1. Run plan_* FIRST (generates spec, no side effects)
2. Check warnings in the plan output
3. If warnings: report back to Solution Architect
4. If clean: proceed to execute_* (subject to approval middleware)
5. After execution: report results with exact logical names created
6. Hand off to Tester for verification or back to Architect

FIELD TYPE EXPERTISE:
[Same detailed field type knowledge as in the tool catalog — the agent must know 
when to suggest email format, phone format, rich text memo, etc.]

BATCH OPERATIONS:
- Group by dependency: independent fields first, then lookups
- Independent fields can execute concurrently
- Lookups must wait for target table to exist
- Report progress: "Created 3/8 fields..."

ERROR HANDLING:
- If create fails: report exact error + what already succeeded
- Don't retry automatically — let Architect decide
- "Name already exists" → check existing, suggest alternative
- "Dependency missing" → check target table/option set exists first
- ALWAYS update OperationStack so undo is possible

[PROJECT STATE INJECTED AT RUNTIME]
```

### 7.3 UI Builder

```
You are the UI Builder for Dataverse AI Forge.

ROLE: User experience specialist. You create forms, views, dashboards, 
and model-driven apps. You translate business requirements into form XML, 
FetchXML, and app configurations.

BOUNDARIES:
✅ Main forms, quick create forms, quick view forms
✅ System views with FetchXML
✅ Dashboards with charts and view components
✅ Model-driven apps with sitemaps
❌ Tables, fields, relationships (→ Data Model Builder)
❌ Plugins, BPFs (→ Automation Builder)
❌ Security, data population, testing, documentation

FORM DESIGN PRINCIPLES:
- Tab 1 "General/Summary": most important fields, always visible
- Tab 2 "Details": secondary fields
- Tab 3 "Related": subgrids for child records
- Tab 4 "Timeline": notes and activities (if enabled)
- Header: max 4 fields (Status, Owner, Created On, Priority)
- Required fields in first tab, first section
- 2-column sections for paired fields (First Name | Last Name)
- 1-column for text areas and subgrids
- Quick Create: max 5-7 fields, no tabs, no subgrids

VIEW DESIGN PRINCIPLES:
- Every table needs minimum: "Active {Table}", "All {Table}", "My {Table}"
- Sort by Created On desc unless a better sort exists
- 5-7 columns max (no horizontal scroll)
- First column = primary name field (linked to record)
- Include status/priority columns for visual scanning

FETCHXML EXPERTISE:
- Use correct operators for each field type
- Link entities for lookup-based filtering
- Option set filtering by VALUE not LABEL (labels differ by language)
- Respect LCID for localized labels

[PROJECT STATE INJECTED AT RUNTIME]
```

### 7.4 Automation Builder

```
You are the Automation Builder for Dataverse AI Forge.

ROLE: Business logic and process automation. You create plugins (C# code gen), 
custom APIs, BPFs, business rules, flow specs, and webhooks.

BOUNDARIES:
✅ Plugin C# code generation + GitHub push
✅ Custom API definitions
✅ Business Process Flows with stages, steps, branching
✅ Business Rules (client-side) with conditions and actions
✅ Power Automate flow specifications (JSON definitions)
✅ Webhook registration
✅ dataverse_runScript as LAST RESORT only
❌ Tables, fields, relationships
❌ Forms, views, dashboards
❌ Security roles
❌ Data population
❌ Direct user communication

PLUGIN CODE STANDARDS:
Every plugin MUST:
- Inherit IPlugin, implement Execute(IServiceProvider)
- Extract IPluginExecutionContext, IOrganizationServiceFactory, ITracingService
- Use ITracingService for ALL logging
- Validate InputParameters before access
- Use try-catch with InvalidPluginExecutionException for business errors
- NEVER use async/await in sync plugins
- NEVER use HttpClient in sync plugins
- NEVER use Thread.Sleep
- NEVER swallow exceptions
- Be idempotent (safe to re-execute)
- Include filtering attributes for Update plugins
- Only request images when needed

PROJECT STRUCTURE:
{SolutionName}.Plugins/
├── Plugins/{EntityName}/{PluginName}.cs
├── Models/ (early-bound)
├── Services/ (shared logic)
├── Constants/ (EntityNames.cs, FieldNames.cs)
└── Tests/{EntityName}/{PluginName}Tests.cs (FakeXrmEasy)

GITHUB WORKFLOW:
1. Feature branch: feature/{entity}-{plugin-name}
2. Commit: plugin + tests + registration info
3. PR: "feat: {PluginName} - {description}"
4. Never push to main directly

BPF DESIGN:
- Stages = real business milestones (not arbitrary steps)
- 2-5 step fields per stage
- Required steps gate stage transitions
- Cross-entity BPFs: max 5 entities, each appears once, need lookup relationships
- Flag limitations to Architect

BUSINESS RULES vs PLUGINS:
- Business rules: client-side only, simple show/hide/set/validate
  → Use for: field visibility, required level, default values, error messages
  → Warning: does NOT protect the API (server-side bypasses business rules)
- Plugins: server-side, complex logic, external calls
  → Use for: anything that must be enforced regardless of entry point
- If server-side enforcement is needed: TELL the Architect that a plugin is required,
  even if the user asked for a business rule.

[PROJECT STATE INJECTED AT RUNTIME]
```

### 7.5 Security Builder (NEW)

```
You are the Security Builder for Dataverse AI Forge.

ROLE: Security specialist. You design and implement the security model for 
Dataverse solutions: security roles, field-level security profiles, and 
column security configuration.

BOUNDARIES:
✅ Security roles (create, modify, clone)
✅ Field-level security profiles
✅ Enable column security on fields
✅ Analyze security gaps
✅ Recommend team structures
❌ Tables, fields, forms, views, plugins, BPFs, data, documentation
❌ Direct user communication

SECURITY DESIGN PRINCIPLES:

### Security Roles
- NEVER modify OOB roles (System Administrator, Basic User, etc.)
  → Always create CUSTOM roles
- Start by cloning an existing role that's close to what you need
  → Then add/remove privileges
- Use the PRINCIPLE OF LEAST PRIVILEGE: only grant what's needed
- Privilege depths:
  → User: can only access own records (most restrictive)
  → Business Unit: own BU records
  → Parent-Child BU: own BU + child BU records
  → Organization: all records (most permissive — use sparingly)
- Common role patterns:
  → "{Entity} Viewer": Read only (User or BU depth)
  → "{Entity} Contributor": Create + Read + Write own records
  → "{Entity} Manager": Full CRUD + Assign + Share at BU level
  → "{Entity} Admin": Full CRUD + Assign + Share at Org level

### Field-Level Security
- Use for PII / sensitive data:
  → Personal: email, phone, address, birthdate, SSN, national ID
  → Financial: salary, bank account, credit card, revenue
  → Medical: health conditions, prescriptions, insurance
  → Legal: case details, settlement amounts
- When to recommend: if a field contains data that not everyone with 
  Read access to the table should see
- Enable IsSecured on the field FIRST, then create the profile
- WARNING: once IsSecured is enabled, it CANNOT be disabled

### What to Check After Every Build Cycle
- Are there custom tables without custom security roles?
  (Users might not have access at all, or might have too much access via inherited roles)
- Are there PII fields without field-level security?
- Are Org-level delete privileges granted on tables with important data?
- Is audit enabled on tables with sensitive data?

OUTPUT FORMAT FOR SECURITY ROLE SPEC:

| Table | Create | Read | Write | Delete | Append | AppendTo | Assign | Share |
|-------|--------|------|-------|--------|--------|----------|--------|-------|
| CustomerFeedback | BU | Org | BU | User | BU | BU | BU | BU |
| FeedbackCategory | — | Org | — | — | Org | Org | — | — |

[PROJECT STATE INJECTED AT RUNTIME]
```

### 7.6 Data Population Agent (NEW)

```
You are the Data Population Agent for Dataverse AI Forge.

ROLE: Data specialist. You populate tables with seed/reference data and 
generate realistic test data. You make the solution usable immediately 
after creation.

BOUNDARIES:
✅ Create seed/reference data records
✅ Generate realistic test data
✅ Clean up test data (only records you created)
✅ Count records
❌ Create/modify tables, fields, forms, views, plugins, security, docs
❌ Direct user communication

TWO MODES:

### 1. Reference/Seed Data
For lookup/reference tables that need initial values.
Examples:
- FeedbackCategory → ["Product Quality", "Customer Service", "Delivery", 
  "Pricing", "User Experience", "Other"]
- Priority → populated via option set (not your job — that's Data Model Builder)
- Country → list of countries with ISO codes
- Department → common department names

Rules:
- Keep seed data minimal and relevant (5-15 records typically)
- Match the environment's base language for labels
- If table has required fields, ALL records must satisfy them
- If table has lookups, reference existing records or seed the target table first

### 2. Test/Sample Data
For main business tables that need realistic records for testing.
Examples:
- CustomerFeedback → 10 records with varied ratings, categories, dates, descriptions
- Contact → 5 records with realistic French names (for FR environments)

Rules:
- Generate REALISTIC data (not "Test 1", "Test 2", "Test 3")
- Respect the locale: French names for FR environments, proper date formats
- Distribute values across choice fields (don't make all records "High Priority")
- Use different dates (not all "today")
- Reference existing seed data for lookups
- Keep descriptions varied and contextually appropriate
- Tag records somehow so they can be cleaned up later 
  (use a description prefix like "[TEST DATA]" or track IDs in ProjectState)

BATCH OPERATIONS:
- Use $batch API for efficiency (up to 1000 operations per batch)
- Report progress: "Created 5/10 test records..."
- If a record fails, continue with the rest and report failures at the end

[PROJECT STATE INJECTED AT RUNTIME]
```

### 7.7 Tester (NEW)

```
You are the Tester for Dataverse AI Forge.

ROLE: Post-execution quality gate. After any builder agent creates or modifies 
components in Dataverse, you verify that the actual result matches the planned 
specification. You are the QA engineer who checks the deployment.

BOUNDARIES:
✅ Read and compare actual Dataverse state vs planned specs
✅ Run smoke tests (form loads? view returns results? BPF stages valid?)
✅ Report discrepancies with exact details
❌ Create, modify, or delete ANYTHING
❌ Fix issues (report them, let Architect decide what to do)
❌ Direct user communication

YOU RUN AFTER EVERY EXECUTION. This is not optional.

VERIFICATION PROCESS:

For each executed operation, you:
1. Re-read the component from the Dataverse API
2. Compare every property against the planned spec
3. Flag any discrepancy: { property, expected, actual }
4. Assign a verdict: PASS, FAIL, or WARNINGS
5. Report back to Solution Architect

WHAT TO CHECK PER COMPONENT TYPE:

Tables: exists, displayName, ownershipType, hasNotes, hasActivities, 
  changeTrackingEnabled, isAuditEnabled, isInSolution
Fields: exists, type matches, maxLength, requiredLevel, isSearchable, 
  isAuditEnabled, format, optionSet values match (for choices)
Relationships: exists, cascadeBehavior matches all 6 types, relatedEntities correct
Forms: exists, allFieldsOnFormExistOnEntity, subgridsValid
Views: exists, fetchXmlValid, allColumnsExist, queryExecutable
BPFs: exists, allStageFieldsExist, crossEntityLookupsValid
SecurityRoles: exists, privilegesMatch

COMPREHENSIVE VALIDATION:
When asked to run_full_validation, check ALL components in ProjectState.
Produce a summary report:
  "22/24 components verified successfully.
   ⚠️ 2 warnings:
   - cr_rating maxLength expected 100, actual 200 (non-critical)
   - View 'By Category' has no results (table is empty, expected)"

IMPORTANT:
- An empty table returning no results is NOT a failure (it's expected if just created)
- A missing field on a form IS a failure (form will show an error)
- A mismatched cascade behavior IS a failure (could cause data issues)
- Report facts, not opinions. Don't speculate on why something failed.

[PROJECT STATE INJECTED AT RUNTIME]
```

### 7.8 Reviewer

```
You are the Reviewer for Dataverse AI Forge.

ROLE: Pre-execution quality gate. You review proposals BEFORE they are executed. 
You are the code reviewer, the architecture reviewer, and the security auditor. 
The one who designs is NOT the one who validates — that's why you exist.

BOUNDARIES:
✅ Validate schema designs against best practices
✅ Review plugin code for quality, security, correctness
✅ Check security implications
✅ Estimate performance impact
❌ Create, modify, or delete ANYTHING
❌ Override the Architect's decisions
❌ Approve or reject on behalf of the user (you recommend, user decides)
❌ Direct user communication

OUTPUT FORMAT:

REVIEW SUMMARY
Score: [0-100]/100
Risk Level: [LOW | MEDIUM | HIGH | CRITICAL]
Verdict: [APPROVED | APPROVED_WITH_WARNINGS | NEEDS_CHANGES | REJECTED]

ISSUES FOUND
🔴 Error — must fix: {description} → {suggestion}
🟡 Warning — should fix: {description} → {suggestion}
🟢 Info — nice to have: {description} → {suggestion}

WHEN TO REJECT (recommend rejection):
- Security vulnerabilities (data exposure, missing auth)
- Destructive operations without safety (cascade delete on large tables)
- Code that will crash at runtime (null reference, type mismatch)
- Violations of Dataverse hard limits
Everything else is a warning — don't block for minor issues.

[PROJECT STATE INJECTED AT RUNTIME]
```

### 7.9 Documentor

```
You are the Documentor for Dataverse AI Forge.

ROLE: Documentation specialist. Every change is documented. If it wasn't 
documented, it didn't happen. You run AUTOMATICALLY after every successful 
build + test cycle.

BOUNDARIES:
✅ Generate data dictionaries, changelogs, ERDs, READMEs, deployment guides
✅ Push documentation to GitHub
✅ Read current schema for accurate documentation
❌ Create or modify Dataverse components
❌ Review or validate designs
❌ Direct user communication

YOU ARE TRIGGERED AUTOMATICALLY. Not optional. Definition of done includes documentation.

DOCUMENTATION STANDARDS:

Data Dictionary: table → fields (with types, required, description) → relationships
Changelog: [version] - date → Added/Changed/Removed sections
ERD: Mermaid erDiagram syntax
README: Overview → Tables → Architecture → Plugins → BPFs → Deployment → Changelog

[PROJECT STATE INJECTED AT RUNTIME]
```

---

## 8. Error Recovery & Undo

### Operation Stack

Every `execute_*` tool pushes to the OperationStack with its reverse operation:

```csharp
public class ExecutedOperation
{
    public string OperationId { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string AgentName { get; set; }
    public string ToolName { get; set; }
    public object Parameters { get; set; }
    public object Result { get; set; }
    public bool IsReversible { get; set; }
    public ReverseOperation? ReverseOp { get; set; }
}

public class ReverseOperation
{
    public string ToolName { get; set; }      // e.g., "delete_table"
    public object Parameters { get; set; }     // e.g., { metadataId: "..." }
    public string Warning { get; set; }        // e.g., "This will delete all records"
    public RiskLevel RiskLevel { get; set; }   // Undo is always at least 🟠
}
```

### Reversibility Matrix

```
┌────────────────────────────┬──────────┬──────────────────────────┐
│ Operation                  │Reversible│ Reverse Action            │
├────────────────────────────┼──────────┼──────────────────────────┤
│ create_table               │ ✅ Yes*  │ delete table (if empty)   │
│ create_field               │ ✅ Yes*  │ delete field              │
│ create_relationship        │ ✅ Yes   │ delete relationship       │
│ create_option_set          │ ✅ Yes   │ delete option set         │
│ add_option_to_option_set   │ ✅ Yes   │ remove option             │
│ create_alternate_key       │ ✅ Yes   │ delete key                │
│ create_form                │ ✅ Yes   │ delete form               │
│ create_view                │ ✅ Yes   │ delete view               │
│ modify_form                │ ✅ Yes   │ restore previous formXml  │
│ create_bpf                 │ ✅ Yes*  │ deactivate + delete       │
│ create_business_rule       │ ✅ Yes   │ deactivate + delete       │
│ create_security_role       │ ✅ Yes   │ delete role               │
│ create_security_profile    │ ✅ Yes   │ delete profile            │
│ populate_reference_data    │ ✅ Yes   │ delete created records    │
│ generate_test_data         │ ✅ Yes   │ delete test records       │
│ push_to_github             │ ⚠️ Partial│ delete branch (not PR)   │
│ modify_table               │ ⚠️ Partial│ restore previous values  │
│ modify_field               │ ⚠️ Partial│ restore previous values  │
│ modify_security_role       │ ⚠️ Partial│ restore previous privs   │
│ delete_table               │ ❌ No    │ Cannot undo deletion      │
│ delete_field               │ ❌ No    │ Cannot undo deletion      │
│ run_script                 │ ❌ No    │ Unknown side effects      │
└────────────────────────────┴──────────┴──────────────────────────┘

* = reversible only if no dependent components were created after
```

### Undo Flow

```
User: "Undo the last action"

Solution Architect:
  → Check OperationStack.Peek()
  → last op: execute_create_field (cr_sentiment on cr_customerfeedback)
  → ReverseOp: delete field cr_sentiment
  → IsReversible: true
  → Check dependencies: is cr_sentiment used in any form, view, or BPF?
    → If yes: "I can't undo this because cr_sentiment is used in the Main Form. 
       Want me to remove it from the form first, then delete the field?"
    → If no: "I'll remove the cr_sentiment field. This cannot be undone. Proceed?"
  → User confirms
  → Execute reverse operation
  → Tester verifies field is gone
  → Update ProjectState

User: "Undo the last 3 actions"

Solution Architect:
  → Show list of last 3 operations with reversibility status
  → Execute in reverse order (LIFO)
  → Check dependencies at each step
```

### Partial Failure Recovery

```csharp
public class BatchExecutionResult
{
    public int TotalOperations { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<OperationResult> Results { get; set; }
    public FailureStrategy RecommendedStrategy { get; set; }
}

public enum FailureStrategy
{
    RetryFailed,        // Only the failed operations can be retried
    SkipAndContinue,    // Proceed without the failed operations
    RollbackAll,        // Undo everything that succeeded
    ManualIntervention  // User needs to decide
}
```

When a batch fails (e.g., creating 8 fields, field 4 fails):

```
Solution Architect:
  "I created 3 of 8 fields successfully. Field 4 (cr_category) failed:
   Error: 'Referenced table cr_feedbackcategory does not exist'
   
   This happened because the Category lookup references a table that 
   hasn't been created yet.
   
   Options:
   1. Create the FeedbackCategory table first, then retry the remaining fields
   2. Skip cr_category and create the other fields, add it later
   3. Undo all 3 created fields and start over
   
   What would you prefer?"
```

### Retry Logic for API Failures

```csharp
public class DataverseRetryPolicy
{
    public static IAsyncPolicy<HttpResponseMessage> GetPolicy() =>
        Policy<HttpResponseMessage>
            .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)  // 429
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: (retryAttempt, response, context) =>
                {
                    // Use Retry-After header if available
                    if (response?.Result?.Headers.RetryAfter?.Delta is TimeSpan delta)
                        return delta;
                    return TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)); // Exponential
                },
                onRetryAsync: (response, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "Retry {Count} after {Delay}s: {Status}",
                        retryCount, timespan.TotalSeconds, response?.Result?.StatusCode);
                    return Task.CompletedTask;
                });
}
```

---

## 9. Session Persistence & Project Management

### Per-Project Sessions

Each project is an independent workspace with its own state, conversation history, and operation log.

```csharp
public class Project
{
    public string ProjectId { get; set; }
    public string ProjectName { get; set; }
    public string UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
    public ProjectState State { get; set; }
    public List<ConversationMessage> ConversationHistory { get; set; } = [];
    public string Summary { get; set; }  // AI-generated summary for quick resume
}
```

### Resume Flow

```
User opens Dataverse AI Forge

CASE 1: Returning user with existing projects
─────────────────────────────────────────────
Solution Architect:
  "Welcome back! You have 2 active projects:
   
   1. 📂 Contoso Feedback (last active: 2 hours ago)
      Environment: contoso-dev.crm4.dynamics.com
      Solution: ContosoFeedbackManagement (cr_)
      Progress: 2 tables, 10 fields, 1 form, 4 views
      Last action: Created 'Active Feedback' view
   
   2. 📂 Sales Extension (last active: 3 days ago)
      Environment: contoso-dev.crm4.dynamics.com
      Solution: ContosoSalesExt (cr_)
      Progress: 1 table, 5 fields
      Last action: Created cr_dealregistration table
   
   Which project would you like to continue?
   Or start a new project."

CASE 2: Resuming a specific project
────────────────────────────────────
Solution Architect:
  "Resuming 'Contoso Feedback'. Last time, we:
   - Created CustomerFeedback and FeedbackCategory tables
   - Set up 10 fields and 2 relationships
   - Created the main form and 4 views
   
   The Tester confirmed everything is valid ✅
   
   What would you like to do next? Some suggestions:
   - Set up a Business Process Flow for the feedback lifecycle
   - Create security roles for feedback managers
   - Add some test data to try it out"

CASE 3: New user
────────────────
  → Standard onboarding flow (environment → solution → ready)
```

### Persistence Implementation

The current implementation uses **in-memory bounded LRU stores** (
`InMemoryProjectStateStore` + `InMemoryConversationStore`, each capped at
100 sessions). The earlier SQLite + Cosmos plan was removed in May 2026
when no deployed path ever exercised it — every test was hitting the
in-memory store anyway, and the SQLite branch had drifted out of sync
with the bounded-LRU semantics. `IProjectStateStore` /
`IConversationStore` are still interfaces, so a persistent backend can be
re-introduced without touching the agent code; the prior `IProjectRepository`
shape can be reconstructed from git history if useful.

### Summary Generation

After each session, the Solution Architect generates a brief summary for the next resume:

```csharp
// Triggered when session ends (browser close, inactivity timeout)
public async Task GenerateSessionSummary(string projectId)
{
    var state = await _stateStore.GetProjectStateAsync(projectId);
    var recentOps = state.OperationStack.Take(10);
    
    // Use Configurable for cheap summarization
    var summary = await _llm.CompleteAsync($"""
        Summarize what was accomplished in this session:
        Operations: {JsonSerializer.Serialize(recentOps)}
        Current state: {state.Tables.Count} tables, {state.Fields.Count} fields, 
        {state.Forms.Count} forms, {state.Views.Count} views
        Keep it to 2-3 sentences.
    """);
    
    await _projectRepo.UpdateSummary(projectId, summary);
}
```

---

## 10. Frontend Architecture & Design System

### Design Philosophy

**NeuralForge Dark** — Solid dark surfaces with clean borders, no glass effects, no blur, no transparency gimmicks. Inspired by Linear, Raycast, and Vercel. The UI looks like a real shipping product, not a demo.

Dual theme support (dark + light) with a toggle in the sidebar footer.

### Layout — 4-Panel Architecture

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                                                                              │
│  ┌─────────┐  ┌──────────────────────┐  ┌─────────────┐  ┌──────────────┐  │
│  │         │  │                      │  │             │  │              │  │
│  │ SIDEBAR │  │     WORKSPACE        │  │    CHAT     │  │ DIAGNOSTICS  │  │
│  │         │  │                      │  │   PANEL     │  │    PANEL     │  │
│  │ 252px   │  │     flex             │  │   348px     │  │   380px      │  │
│  │         │  │                      │  │             │  │              │  │
│  │ collaps │  │  Dashboard           │  │  Agent msgs │  │  Tool calls  │  │
│  │ ible →  │  │  Table Detail        │  │  Approvals  │  │  Payloads    │  │
│  │ 52px    │  │  ERD Diagram         │  │  SRS cards  │  │  Results     │  │
│  │         │  │  Activity Log        │  │  Activity   │  │  Timing      │  │
│  │         │  │                      │  │             │  │              │  │
│  │ [☀/🌙] │  │                      │  │ [🐛] [✕]   │  │ [Connected]  │  │
│  │ [◀/▶]  │  │                      │  │             │  │              │  │
│  └─────────┘  └──────────────────────┘  └─────────────┘  └──────────────┘  │
│                                                                              │
│  When chat is closed: floating gradient ◉ button bottom-right to reopen     │
│  When sidebar collapsed: 52px icon-only rail                                 │
│  Diagnostics: toggleable from chat header 🐛 button                         │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Panel Behaviors

| Panel | Default | Collapsible | How |
|-------|---------|-------------|-----|
| **Sidebar** | 252px expanded | Yes → 52px icon rail | Chevron button in sidebar footer |
| **Workspace** | flex (fills remaining) | No | Always visible |
| **Chat Panel** | 348px visible | Yes → hidden | X button in chat header; floating FAB to reopen |
| **Diagnostics** | Hidden | Yes → 380px | Bug icon in chat header toggles on/off |

When chat is hidden, the workspace expands to fill. When diagnostics is open, it appears to the right of chat. All four panels can coexist simultaneously.

### Design Tokens

#### Dark Theme (default)

```
Background tiers:
  bg0: #0c0d10    // void — outermost background
  bg1: #131416    // sidebar, chat panel, diagnostics
  bg2: #1a1b1f    // cards, elevated surfaces, table headers
  bg3: #212226    // hover states, input backgrounds, agent badges
  bg4: #2a2b30    // active highlights, pressed states

Borders:
  border:   #262730    // default card/panel borders
  borderLt: #1e1f24   // subtle row separators
  borderHi: #34353c   // focus/hover border

Text:
  t1: #ecedf0    // primary text (headings, field names)
  t2: #9294a0    // secondary text (descriptions, agent messages)
  t3: #5c5e6a    // muted text (timestamps, labels)
  t4: #3a3c46    // ghost text (placeholders, disabled)

Semantic colors:
  green:    #3dcc8e / greenBg: #162d22    // success, created, verified
  orange:   #e5983e / orangeBg: #2d2218   // warnings, BPF stages, slow tools
  red:      #e05565 / redBg: #2d1a1e      // errors, reject, high risk
  blue:     #5b9cf5 / blueBg: #182238     // info, field types, links
  purple:   #a385f0 / purpleBg: #221c34   // BA agent, relationships, reviews

Code:
  codeBg:   #0e0f13
  codeText: #b0b8c8
```

#### Light Theme

```
Background tiers:
  bg0: #f5f5f7    // outermost
  bg1: #ffffff    // panels
  bg2: #f0f0f3    // cards
  bg3: #e8e8ec    // hover
  bg4: #dddde2    // active

Borders:
  border: #e0e0e5 / borderLt: #eaeaef / borderHi: #d0d0d6

Text:
  t1: #1a1b1e / t2: #5c5e6a / t3: #8c8e98 / t4: #b0b2ba

Semantic colors (deeper for contrast on light backgrounds):
  green:  #1a9a62 / greenBg: #e6f7ef
  orange: #c47a1a / orangeBg: #fdf3e4
  blue:   #3a78d8 / blueBg: #e8f0fd
  purple: #7a5cc8 / purpleBg: #f0eafe
  red:    #c84050 / redBg: #fce8eb
```

### Typography

| Use | Font | Weight | Size |
|-----|------|--------|------|
| UI text | Geist (Vercel) | 400–700 | 10–15px |
| Code / logical names | JetBrains Mono | 400–500 | 9–12px |
| Page titles | Geist | 700 | 28px, -0.8 tracking |
| Section headers | Geist | 620 | 14px |
| Pills / badges | Geist | 500 | 11px |
| Sidebar items | Geist | 420–550 | 12.5–13px |

### Agent Identity System

Each agent has a unique icon + color. Displayed as a small square badge (22px, 6px radius, bg3 background).

| Agent | Icon | Color |
|-------|------|-------|
| Solution Architect | Layers | blue |
| Business Analyst | MessageSquare | purple |
| Data Model Builder | Database | green |
| UI Builder | LayoutDashboard | blue |
| Automation Builder | Zap | orange |
| Security Builder | Shield | red |
| Data Population | BarChart3 | orange |
| Tester | TestTube | green |
| Reviewer | Eye | purple |
| Documentor | BookOpen | muted (t3) |

Icons from Lucide React. No emojis anywhere in the UI.

### Sidebar Components

```
┌─────────────────────────────┐
│ [Logo] Dataverse AI Forge          │  Brand + gradient icon
│         Dataverse Builder    │
├─────────────────────────────┤
│ [🔍] Search...        ⌘K   │  Search bar
├─────────────────────────────┤
│ ▪ Overview                   │  Nav items with active highlight
│ ▪ Activity Log          10  │  Count badges
│ ▪ ERD Diagram               │
├─────────────────────────────┤
│ [📦] Contoso Feedback        │  Solution badge with prefix + version
│       cr_ · v1.0.0.0        │
├─────────────────────────────┤
│ ▸ TABLES                  2 │  Collapsible tree sections
│   ▪ Customer Feedback     8 │  Table items with field count
│     ├ Name (AutoNumber)      │  Expandable field list
│     ├ Rating (Choice)        │
│     └ ...                    │
│   ▪ Feedback Category     3 │
│ ▸ COMPONENTS              8 │
│ ▸ SECURITY                2 │
├─────────────────────────────┤
│ ● contoso-dev.crm4          │  Environment status
│   Development                │
│              [☀] [◀]        │  Theme toggle + collapse button
└─────────────────────────────┘
```

When collapsed (52px): shows only icons for nav items, logo icon, search icon, theme/collapse buttons.

### Workspace Views

**Dashboard (default)**
- Solution title + prefix + version + "All verified" pill
- 5 stat cards in a grid: Tables, Fields, UI, Logic, Security (large number + icon)
- Component cards: 2×2 grid showing tables, BPF, plugin with status pills
- Recent Activity: last 5 log entries in alternating-row table

**Timeline** (new workspace tab)
- Horizontal Gantt chart of agent activity across time
- See "Agent Timeline / Gantt View" section above for full spec

**Table Detail** (click a table in sidebar)
- Header: logical name + "Created" pill + table display name (28px)
- Metadata pills: field count, relationship count, ownership type, audit status
- Fields table: bordered, rounded corners, grid layout (Field | Logical | Type | Required | Status)
- Hover rows highlight to bg2
- Type pills in blue, required checkmarks, green verified checkmarks
- Relationships section: cards with entity name, type pill (N:1), foreign key, cascade info

**ERD Diagram**
- Dot-grid background (radial gradient pattern)
- Entity cards with colored borders (blue for primary, green for related, dimmed for OOB)
- Relationship lines with labels (N:1, 1:N)
- Field previews inside each card

**Activity Log**
- Full-width alternating-row table
- Columns: timestamp (mono), agent badge, terminal icon + tool name (mono), duration (mono), status checkmark
- Handoff events shown in blue text

### Chat Panel

**Header**: Sparkles icon + "Assistant" title + agent badge row (first 4 agents + "+6") + diagnostics toggle + close button

**Message types**:

| Type | Rendering |
|------|-----------|
| User message | Right-aligned bubble, blueBg background, rounded corners (12/12/3/12) |
| Agent message | Left-aligned with agent badge + name, bg2 bubble, rounded (3/12/12/12) |
| SRS Card | Purple-tinted card with BookOpen icon, requirements as chevron-prefixed list |
| Approval Card | Green (approved) or orange (pending) card with action buttons: Approve, Edit, Reject |
| Activity Row | Compact agent badge + action text list |
| Handoff | Inline agent transfer notice |

**Input**: Paperclip icon + text input + gradient send button (blue→purple)

### Diagnostics Panel (Debug / Assistant View)

Toggled from the chat header's bug icon. Shows real-time technical details of every agent tool call.

```
┌─────────────────────────────────────┐
│ [🐛] Diagnostics    [Connected ●]  │
├─────────────────────────────────────┤
│                                     │
│  ┌─────────────────────────────┐    │
│  │ [🧠] Solution Architect     │    │  Agent badge + name
│  │ 22:41:03            234ms   │    │  Timestamp + duration pill
│  │                              │    │  (green < 1s, orange > 1s)
│  │ ⌨ get_environment_info      │    │  Tool name in monospace
│  │                              │    │
│  │ ▸ Payload                    │    │  Collapsible JSON section
│  │   {                          │    │
│  │     "endpoint": "https://..  │    │  Syntax-highlighted JSON
│  │     "api": "/api/data/v9.2"  │    │
│  │   }                          │    │
│  │                              │    │
│  │ ▸ Result                     │    │  Collapsible JSON section
│  │   {                          │    │
│  │     "orgId": "a1b2c3d4",     │    │
│  │     "baseLang": 1036,        │    │
│  │     "version": "9.2.24013"   │    │
│  │   }                          │    │
│  └─────────────────────────────┘    │
│                                     │
│  ┌─────────────────────────────┐    │
│  │ [📐] Data Model Builder     │    │
│  │ 22:42:17           1847ms   │    │
│  │ ⌨ execute_create_table      │    │
│  │ ▸ Payload                    │    │
│  │ ▸ Result                     │    │
│  └─────────────────────────────┘    │
│                                     │
│  ... (scrollable, auto-scrolls)     │
└─────────────────────────────────────┘
```

Each diagnostic entry contains:
- **Agent identity** — badge + colored name
- **Timing** — monospace timestamp + colored duration pill (green ≤ 1s, orange > 1s)
- **Tool name** — terminal icon + monospace name
- **Payload** — collapsible JSON block showing the request parameters sent to the tool
- **Result** — collapsible JSON block showing the tool's response

Code blocks use `codeBg` / `codeText` tokens, JetBrains Mono, max-height 180px with scroll, `pre-wrap` for long values.

This panel is **100% custom**. We capture tool events via the SSE `TOOL_CALL_*` events processed in `useForgeChat` and derive `DiagnosticEntry[]` in `useDiagnostics()`.

### Agent Thinking / Reasoning Trace

When an agent is reasoning — the Architect deciding which agent to involve, the BA formulating questions, the Reviewer scoring a schema — the chat shows a collapsible "thinking" block before the final response. This gives transparency into WHY agents make decisions, not just what they output.

```
┌──────────────────────────────────────┐
│  [🧠] Solution Architect             │
│                                      │
│  ▸ Reasoning                         │  ← collapsed by default
│  ┌────────────────────────────────┐  │
│  │ The user wants a feedback       │  │  dimmed text (t3), italic
│  │ module. This is a broad request │  │  bg2 background with dashed
│  │ with multiple unknowns:         │  │  left border in agent color
│  │ - Who submits? Not specified    │  │
│  │ - Rating scale? Not specified   │  │
│  │ - Review process? Unknown       │  │
│  │ → Routing to Business Analyst   │  │
│  │   for requirement elicitation   │  │
│  └────────────────────────────────┘  │
│                                      │
│  Let me connect you with the         │  ← actual response (normal t2)
│  analyst to refine requirements.     │
└──────────────────────────────────────┘
```

**Design rules:**
- Collapsed by default — shows "▸ Reasoning" label with agent color left accent
- Expanded: dimmed text (`t3`), slightly smaller font (11px), `bg2` background, `codeBg` tint, 3px dashed left border in agent color
- Always appears ABOVE the agent's actual response message
- Only shown when the backend emits reasoning content (not every message has it)
- Diagnostics panel shows the raw reasoning alongside tool calls for full transparency

**AG-UI integration:** The backend emits reasoning as a `STATE_DELTA` event with key `reasoning` before the `TEXT_MESSAGE` events. The frontend captures it via `useAgent()` state and attaches it to the corresponding message.

### Agent Timeline / Gantt View

A new workspace tab showing a horizontal timeline of agent activity. Makes multi-agent orchestration visually obvious — essential for demos and debugging.

```
┌──────────────────────────────────────────────────────────────────────┐
│  Agent Timeline                              Total: 12.4s  [$0.032] │
│                                                                      │
│  TIME →  0s    2s    4s    6s    8s   10s   12s                      │
│  ─────────────────────────────────────────────────────────────────── │
│                                                                      │
│  Architect   ████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░█████                │
│              ↑ plan                              ↑ summary           │
│                                                                      │
│  BA          ░░░████████████░░░░░░░░░░░░░░░░░░░░░░░░                │
│              ↑ eliciting requirements (user turns)                    │
│                                                                      │
│  Reviewer    ░░░░░░░░░░░░░░████░░░░░░░░░░░░░░░░░░░░                │
│              ↑ validate_schema  892ms                                 │
│                                                                      │
│  Data Model  ░░░░░░░░░░░░░░░░░░██████████░░░░░░░░░░                │
│              ↑ create_table     ↑ create_field ×8                    │
│              1847ms             3201ms                                │
│                                                                      │
│  Tester      ░░░░░░░░░░░░░░░░░░░░░░░░░░░░████░░░░░░                │
│              ↑ full_validation  2103ms                                │
│                                                                      │
│  Documentor  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░████░░                │
│              ↑ push_docs                                             │
│                                                                      │
│  ─────────────────────────────────────────────────────────────────── │
│  ● = active (agent color)  ░ = idle  🛑 = approval wait             │
│                                                                      │
│  Hover any bar → tooltip: tool name, duration, status                │
│  Click any bar → jump to that entry in diagnostics panel             │
└──────────────────────────────────────────────────────────────────────┘
```

**Design rules:**
- Accessible as a new workspace tab: Dashboard | **Timeline** | Activity Log | ERD
- Each agent gets a horizontal row, colored with their agent color
- Active segments are solid bars, idle is empty
- Approval waits shown as a red stop marker (🛑) with duration
- User turn pauses (BA conversation) shown as hatched/lighter bars
- Hover a bar → tooltip with tool name, duration, payload summary
- Click a bar → scrolls the diagnostics panel to that entry (cross-panel linking)
- Top-right shows total elapsed time + total session cost
- Auto-scrolls right as the session progresses; zoom in/out with scroll wheel
- Supports replay: a playhead scrubber to re-watch the agent activity sequence

**Data source:** Built from the `toolCalls` captured by `useForgeChat` plus `STATE_DELTA` events from `useAgent()` that track agent transitions. Each event has a timestamp, so the timeline is reconstructed client-side.

### Token & Cost Tracker

A persistent, compact cost indicator showing real-time Azure AI Foundry usage for the current session. Proves the ROI story: "this entire module cost $0.47 to build."

**Location:** Two placements:
1. **Compact pill** in the sidebar footer — always visible: `⚡ 34.2k tokens · $0.47`
2. **Expanded panel** in the workspace (new tab or overlay) — detailed breakdown

**Compact display (sidebar footer):**
```
┌──────────────────────────────────┐
│ ● contoso-dev.crm4               │
│   Development                    │
│ ⚡ 34.2k tokens · $0.47          │  ← new line, clickable
│              [☀] [◀]            │
└──────────────────────────────────┘
```

**Expanded breakdown (click to expand or workspace tab):**
```
┌──────────────────────────────────────────────────────┐
│  Session Cost                              $0.47     │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │ Agent           │ Model   │ Tokens │ Cost      │  │
│  │─────────────────│─────────│────────│───────────│  │
│  │ Architect        │ GPT-4o  │ 8,420  │ $0.126   │  │
│  │ Business Analyst │ GPT-4o  │ 5,230  │ $0.078   │  │
│  │ Data Model Bld.  │ 4o-mini │ 6,100  │ $0.009   │  │
│  │ Reviewer         │ 4o-mini │ 3,200  │ $0.005   │  │
│  │ Tester           │ 4o-mini │ 4,800  │ $0.007   │  │
│  │ UI Builder       │ 4o-mini │ 3,100  │ $0.005   │  │
│  │ Automation Bld.  │ GPT-4o  │ 2,400  │ $0.036   │  │
│  │ Documentor       │ 4o-mini │ 1,000  │ $0.002   │  │
│  │─────────────────│─────────│────────│───────────│  │
│  │ TOTAL            │ mixed   │ 34.2k  │ $0.47    │  │
│  └────────────────────────────────────────────────┘  │
│                                                      │
│  ⏱ Session duration: 12m 34s                         │
│  📊 API calls: 42                                    │
│  ⚡ Avg latency: 1.2s                                │
│                                                      │
│  💡 "Manual equivalent: ~3 days / ~$4,800 consultant │
│     AI cost: $0.47 — 10,000× cheaper"                │  ← the demo killer line
└──────────────────────────────────────────────────────┘
```

**Design rules:**
- Compact pill uses `t3` text, Zap icon, monospace numbers
- Updates in real-time as tokens are consumed (debounced every 500ms)
- Agent rows sorted by cost descending
- Color-code the model column: blue for GPT-4o, green for Configurable
- The "manual equivalent" comparison line is the conference mic-drop moment
- Cost calculation: input tokens × model rate + output tokens × model rate per agent per call

**Data source:** The backend includes token usage in `TOOL_CALL_END` and `RUN_FINISHED` AG-UI events via a custom metadata field. The frontend accumulates these in a React state reducer.

### Component Library (Primitives)

| Component | Description |
|-----------|-------------|
| `Pill` | Inline badge with icon + text, colored or neutral |
| `AgentDot` | Square badge (22px) with agent icon in agent color |
| `StatusDot` | Circular dot (7px) for status indicators |
| `IconBtn` | 28px square button with icon, border, hover state, active state |
| `CodeBlock` | Pre-formatted JSON viewer with theme-aware colors |

### Prototype Artifacts

| Version | File | Status |
|---------|------|--------|
| v3 | `dvforge-ui-v3.jsx` | Atmospheric gradient mesh — rejected (too artsy) |
| v4 | `dvforge-ui-v4.jsx` | Pure black monochrome — rejected (too dark) |
| v5 | `dvforge-ui-v5.jsx` | Increased transparency — rejected (still too dark) |
| v6 | `dvforge-ui-v6.jsx` | Apple liquid glass — rejected (too busy/ugly) |
| v7 | `dvforge-ui-v7.jsx` | NeuralForge dark, solid surfaces — **approved direction** |
| v8 | `dvforge-ui-v8.jsx` | v7 + dark/light theme toggle |
| v9 | `dvforge-ui-v9.jsx` | **Current** — v8 + collapsible sidebar + collapsible chat + diagnostics panel |

---

## 11. Frontend SSE Integration

### Architecture

The frontend uses a **custom SSE client** (`ForgeChatProvider` + `useForgeChat`) to consume AG-UI events directly from the .NET backend. All UI is custom — no third-party chat frameworks. The AG-UI protocol bridges the .NET backend to the React frontend via a thin Next.js proxy route.

```
┌────────────────────────────────────────────────────────────────┐
│  Dataverse AI Forge Frontend (React + Custom UI)               │
│                                                                │
│  <ForgeChatProvider sessionId={threadId}>                      │
│                                                                │
│  Hooks used:                                                   │
│  ├── useForgeChatContext()     → Chat messages + streaming     │
│  ├── useDiagnostics()         → Tool calls → diag + timeline  │
│  ├── useForgeApprovals()      → Human-in-the-loop approvals   │
│  ├── useAgent()               → Shared state + reasoning       │
│  │                               trace + agent transitions     │
│  └── useTokenTracker()        → Accumulates token/cost data   │
│                                                                │
└──────────────────────┬─────────────────────────────────────────┘
                       │ AG-UI Protocol (SSE events)
                       ▼
┌────────────────────────────────────────────────────────────────┐
│  Next.js Proxy (/api/chat → backend /api/copilotkit)           │
│  Transparent SSE pass-through                                  │
└──────────────────────┬─────────────────────────────────────────┘
                       │ HTTP/SSE
                       ▼
┌────────────────────────────────────────────────────────────────┐
│  Dataverse AI Forge Backend (.NET 10 + Azure AI Foundry)           │
│  Microsoft Agent Framework — 10 agents                         │
│  Emits AG-UI events: TEXT_MESSAGE, TOOL_CALL, STATE_DELTA      │
└────────────────────────────────────────────────────────────────┘
```

### Key Integration Points

**Chat Panel → `useForgeChatContext()`**
- `messages` array contains all user + assistant messages
- `sendMessage()` sends user input, initiates SSE stream
- `isStreaming` drives streaming indicator
- Custom `MessageRenderer` maps messages to AgentBubble, SrsCard, ApprovalCard, ActivityRow

**Diagnostics Panel → `useDiagnostics()`**
- Derives `DiagnosticEntry[]` from `toolCalls` captured by `useForgeChat`
- Events fed to DiagPanel component (100% custom)
- Tool name resolved to agent via static `toolToAgent` map

**Approval Cards → `useForgeApprovals()`**
- Detects `request_approval` / `RequestApprovalAsync` tool calls from SSE TOOL_CALL events
- ApprovalCard renders Approve/Edit/Reject buttons
- User decision sent as next chat message → agent reads and continues

**Shared State → `useAgent()`**
- Derives phase, tables, relationships from `agentState` in `ForgeChatContext`
- `agentState` updated via STATE_DELTA SSE events: `{ activeAgent, solutionState, reasoning }`
- Sidebar tree auto-updates when agents create new tables
- Phase drives UI indicators: idle → design → building → reviewing → testing → documenting

### AG-UI Event Mapping

| AG-UI Event | Frontend Handler | UI Effect |
|-------------|-----------------|-----------|
| `TEXT_MESSAGE_START/CONTENT/END` | `useForgeChat` | Stream agent bubble |
| `TOOL_CALL_START/ARGS/END/RESULT` | `useForgeChat` → `useDiagnostics` | Diagnostics entries + timeline bars |
| `STATE_DELTA` (path: `/solutionState`) | `useForgeChat` → `useAgent` | Update tables, sidebar tree |
| `STATE_DELTA` (path: `/reasoning`) | `useForgeChat` → `useAgent` | Populate thinking trace block above next message |
| `STATE_DELTA` (path: `/activeAgent`) | `useForgeChat` → `useAgent` | Timeline: start new agent bar, update sidebar indicator |
| `RUN_STARTED/FINISHED` | `useForgeChat` | Loading indicators, timeline session bounds |
| `RUN_FINISHED` (metadata: `usage`) | `useForgeChat` → `useTokenTracker` | Accumulate tokens + cost per agent |

### Custom Components

| Component | Description |
|-----------|-----------|
| Chat panel UI | Themed bubbles, SRS cards, approval cards, thinking trace blocks |
| Diagnostics panel | Tool event capture derived from SSE TOOL_CALL events |
| Agent timeline | Gantt visualization built from tool call + state events |
| Token/cost tracker | Custom reducer accumulating usage metadata from AG-UI events |
| Sidebar tree | Auto-refreshes from agent state — fully custom navigation |
| Workspace views | Dashboard, timeline, table detail, ERD, activity log — all custom |
| Theme system | Dark/light tokens, Geist + JetBrains Mono typography |

### Frontend File Structure

```
src/
├── app/
│   ├── providers.tsx           # ForgeChatProvider + ThemeProvider wrapper
│   ├── page.tsx                # Dataverse AI Forge main layout
│   └── api/chat/route.ts      # Thin SSE proxy → backend /api/copilotkit
│
├── components/
│   ├── layout/
│   │   ├── Sidebar.tsx         # Collapsible sidebar with solution tree
│   │   ├── ForgeChatProvider   # (in hooks/useForgeChat.tsx)
│   │   └── ThemeProvider.tsx   # Dark/light theme context
│   ├── chat/
│   │   ├── ChatPanel.tsx       # Custom chat shell → useForgeChatContext
│   │   ├── MessageRenderer.tsx # Routes messages to correct component
│   │   ├── AgentBubble.tsx     # Agent message with identity badge
│   │   ├── ThinkingTrace.tsx   # Collapsible reasoning block (dimmed, dashed border)
│   │   ├── SrsCard.tsx         # Requirements specification card
│   │   ├── ApprovalCard.tsx    # Human-in-the-loop approval gate
│   │   └── ActivityRow.tsx     # Compact agent action summary
│   ├── diagnostics/
│   │   ├── DiagPanel.tsx       # Diagnostic log panel
│   │   ├── DiagEntry.tsx       # Single tool call entry
│   │   └── CodeBlock.tsx       # JSON payload/result viewer
│   ├── workspace/
│   │   ├── Dashboard.tsx       # Solution overview with stats
│   │   ├── AgentTimeline.tsx   # Gantt chart of agent activity
│   │   ├── TableDetail.tsx     # Field table + relationships
│   │   ├── ErdDiagram.tsx      # Entity relationship visualization
│   │   ├── ActivityLog.tsx     # Full activity log view
│   │   └── TokenPanel.tsx      # Expanded cost breakdown table
│   └── primitives/
│       ├── Pill.tsx, AgentDot.tsx, IconBtn.tsx, StatusDot.tsx
│
├── hooks/
│   ├── useForgeAgent.ts    # useAgent wrapper with typed state + reasoning
│   ├── useForgeApprovals.ts # Approval action handlers
│   ├── useDiagnostics.ts      # Wildcard tool call capture → diag + timeline
│   ├── useTokenTracker.ts     # Token/cost accumulator reducer
│   └── useTheme.ts            # Theme context hook
│
├── lib/
│   ├── themes.ts              # Dark + light token objects
│   ├── agents.ts              # Agent definitions (icons, colors, names)
│   ├── pricing.ts             # Model pricing rates for cost calculation
│   └── types.ts               # TypeScript types for agent state
│
└── styles/globals.css          # Scrollbar, selection, base styles
```

---

## 12. MVP Implementation Roadmap

### Updated Tool Count: Tier 1

```
Business Analyst:    0 tools (conversation only — no Dataverse access)
Solution Architect:  14 tools (read + solution management)
Data Model Builder:  16 tools (plan + execute for tables, fields, relationships, options)
UI Builder:          8 tools (plan + execute for forms, views)
Automation Builder:  6 tools (plan BPF + plan plugin + GitHub push)
Security Builder:    6 tools (read roles + plan/execute create role + analyze gaps)
Data Population:     4 tools (plan/execute seed data + generate test data)
Tester:             8 tools (verify table, fields, relationships, form, view, BPF, role, full)
Reviewer:            2 tools (validate schema + validate plugin code)
Documentor:          4 tools (data dictionary, changelog, ERD, GitHub push)
─────────────────────────────────
TOTAL TIER 1:       68 tools (+ BA conversational agent)
```

### Phase 0 — Foundation (Week 1-2)

```
Goal: Skeleton compiles, one agent responds, Dataverse authenticates.
Exit: Full onboarding flow works (environment → solution → ready).

Backend:
- .NET 10 solution scaffold (Clean Architecture)
- Azure AI Foundry integration (GPT-4o + Configurable)
- Dataverse MSAL auth + HttpClient
- Solution Architect agent: get_environment_info, get_solutions_list,
  get_publishers, plan_create_solution, execute_create_solution
- AG-UI SSE event emitter (TEXT_MESSAGE, TOOL_CALL, STATE_DELTA)
- Project persistence (SQLite)
- docker-compose

Frontend:
- Next.js project scaffold
- ForgeChatProvider + /api/chat SSE proxy route
- useForgeChatContext() → custom chat panel with SSE streaming
- Design system: Geist + JetBrains Mono fonts, dark theme tokens
- Sidebar with solution tree (static initially)
- Theme toggle (dark/light)
- Environment selector
```

### Phase 1 — Schema Loop (Week 3-4)

```
Goal: BA + Architect + Data Model Builder + Reviewer + Tester create a table.
Exit: "Create a CustomerFeedback table" works end-to-end with review + test.

Backend:
- Business Analyst agent (conversational, GPT-4o, requirement spec output)
- Solution Architect: get_tables_list, get_table_details, validate_schema_name,
  generate_erd_mermaid, check_oob_table_coverage
- Data Model Builder: plan/execute for table, field (8 types), 1:N relationship
- Reviewer: validate_schema_design
- Tester: verify_table, verify_field, verify_relationship
- Handoff orchestration (HandoffBuilder, 5 agents including BA)
- Project State + middleware (injection, operation stack)
- Approval middleware (risk classifier)
- Error recovery (partial failure handling, retry)

Frontend:
- useDiagnostics() → diagnostics panel with payload/result JSON (derived from SSE TOOL_CALL events)
- useForgeApprovals() → approval cards with Approve/Edit/Reject (from request_approval tool call)
- useAgent() → shared state (current agent, phase, tables, reasoning)
- useTokenTracker() → per-agent token/cost accumulation from RUN_FINISHED metadata
- SRS card renderer (BA requirement specs)
- ThinkingTrace component — collapsible reasoning blocks in chat
- Agent identity system (10 colored badges with Lucide icons)
- Sidebar tree auto-refresh from agent state
- ERD diagram view
- Collapsible sidebar (52px icon rail)
- Collapsible chat panel (floating FAB to reopen)
```

### Phase 2 — Full Agent Team (Week 5-6)

```
Goal: All 10 agents operational. Forms, views, BPFs, plugins, security, data.
Exit: Full "feedback module" demo works.

Backend:
- UI Builder: plan/execute form + view
- Automation Builder: plan BPF + plan plugin + GitHub push
- Security Builder: get_roles, plan/execute create role, analyze_gaps
- Data Population: seed data + test data
- Tester: verify form, view, BPF, security role, full validation
- Reviewer: validate_plugin_code
- Documentor: data dictionary, changelog, ERD, GitHub push
- Full 10-agent handoff orchestration
- Undo support (operation stack reversal)

Frontend:
- Light theme polish
- Dashboard view with stat cards + component grid
- Agent Timeline / Gantt view (horizontal bars per agent, cross-linked to diagnostics)
- Token & Cost Tracker — compact pill in sidebar + expanded breakdown panel
- useTokenTracker hook (accumulates usage from AG-UI metadata)
- Table detail view (field table, relationships)
- Activity log view (full tool call history)
- Code preview in approval cards (plugin review)
- BPF stage visualizer
- Diagnostics panel: timing analysis, error highlighting
```

### Phase 3 — Polish & Demo (Week 7-8)

```
Goal: Conference-ready demo.

- Session persistence (resume projects)
- Project selector UI
- Agent activity animations (streaming dots, phase transitions)
- Timeline replay mode (playhead scrubber to re-watch agent sequence)
- Token tracker: "manual equivalent" comparison line for demo impact
- OpenTelemetry + Aspire dashboard
- Diagnostics panel: filter by agent, search tool names
- Demo script rehearsal with all panel states
  (sidebar collapsed, diagnostics open, timeline view, cost breakdown,
   thinking trace expanded, light/dark toggle)
- Fallback recordings
- README + open source prep
```

---

## 13. Demo Script (10 Minutes)

```
0:00 — INTRO
"What if your D365 project team was AI-powered?"
Architecture slide: 10 agents, handoff pattern, custom SSE + AG-UI.

0:30 — CONNECT & ONBOARD
Open Dataverse AI Forge → dark theme, solid design, sidebar with solution tree.
Connect to environment → select/create solution.
Show: solution context in sidebar (prefix, version, environment).
Show: token counter at $0.00 in sidebar footer.
Toggle light mode briefly → toggle back to dark.

1:30 — DISCOVER
"What tables do I have?" → Architect inspects.
Show: thinking trace expands — "Checking environment for existing tables..."
Open diagnostics panel (bug icon) → show get_environment_info tool call
with payload JSON and result JSON expanding in real-time.
Show: tool duration pills (green < 1s, orange > 1s).
Show: token counter ticks up to $0.02.

2:30 — REQUIREMENTS (BA)
"I need a feedback module with ratings, categories, and follow-ups."
→ Architect thinking trace: "Broad request with unknowns → routing to BA"
→ Business Analyst takes over (purple badge), asks clarifying questions:
  "Who submits feedback?", "What rating scale?", "Review process?"
→ User answers → BA produces structured SRS card in chat.
→ BA hands off to Architect with clear requirements.

4:00 — DESIGN & REVIEW
→ Architect designs schema, shows ERD (dot-grid diagram auto-populates).
→ Reviewer thinking trace: "Checking naming conventions, audit settings,
  cascade rules..." → scores 94/100 with 2 warnings.
→ Approval card appears: Approve / Edit / Reject.
→ User approves.
Show: diagnostics panel shows validate_schema_design payload + result.

5:00 — CREATE SCHEMA
→ Data Model Builder creates 2 tables, 10 fields, 2 relationships.
→ Sidebar tree updates LIVE as tables are created.
→ Tester verifies: "22/22 passed ✅"
Show: click table in sidebar → table detail view with fields grid.
Show: diagnostics panel scrolling with create_table, create_field ×8 calls.

6:30 — UI
"Create a form and some views."
→ UI Builder creates form + 4 views.
Show: open Dataverse, show the actual form.

7:30 — AUTOMATION
"Add a BPF: New → Review → Action → Closed. And a plugin for SLA."
→ BPF created. Plugin code generated.
→ User reviews code in approval card.
→ Pushed to GitHub.
Show: GitHub PR.

8:30 — SECURITY & DATA
"Set up security roles and add some test data."
→ Security Builder creates Feedback Manager + Feedback Viewer roles.
→ Data Population creates 5 categories + 10 test feedback records.
→ Tester verifies everything.

9:00 — DOCUMENTATION
→ Documentor auto-generates data dictionary + ERD + changelog.
→ Pushed to GitHub.

9:15 — THE SHOWCASE
Switch to Timeline view → show the Gantt chart of all agent activity:
  "Here's every agent that worked on this. Architect planned, BA gathered
   requirements, Data Model built the schema, Tester verified, all 
   running in parallel where possible."
Hover bars → show tool durations. Click a bar → jump to diagnostics.

Click token tracker in sidebar → expand cost breakdown:
  "10 agents, 42 API calls, 34,000 tokens. Total cost: $0.47.
   The manual equivalent? About 3 days of consultant time at $4,800.
   That's 10,000× cheaper."
  (pause for audience reaction)

Collapse sidebar → maximize workspace. Close chat → full-width table detail.
Reopen chat via floating FAB → reopen diagnostics → all 4 panels visible.
Toggle light mode → everything adapts.

9:45 — SUMMARY
"In under 10 minutes: 2 tables, 12 fields, 2 relationships, 1 form,
 4 views, 1 BPF, 1 plugin with tests, 2 security roles, 15 data records,
 full documentation. All reviewed, tested, and version-controlled.
 10 specialized AI agents. One conversation. Forty-seven cents."

10:00 — CTA
Open source announcement. Links. Q&A.
```

---

## 14. Future Considerations (Post-MVP)

| Feature | Description | Priority |
|---------|-------------|----------|
| **Translation Agent** | Multi-language labels (FR/EN), XLIFF export/import | High (your env is FR) |
| **ALM Awareness** | Solution export, version bumping, env variables, deployment runbook | High |
| **Voice Input** | Azure Speech → text → chat. Hands-free requirement dictation, especially powerful for live demos and accessibility | Medium |
| **Solution Templates** | DEFERRED per decision — no templates for now | — |
| **Power Fx Plugins** | Suggest low-code plugins for simple logic | Medium |
| **Modern Commanding** | Custom command bar buttons | Medium |
| **Canvas/PCF** | PCF control scaffolding, canvas app layouts | Low |
| **Collaboration** | Multi-user, locking, activity feed | Low |
| **Copilot Studio Gen** | Generate Copilot Studio topics as output | Low |
| **pac CLI Gen** | Generate pac CLI scripts as alternative output | Low |
| **Semantic Kernel Hybrid** | Use SK MagenticOrchestration if Handoff proves limiting | Fallback |

---

## Document History

| Version | Date | Changes |
|---------|------|---------|
| v1 | 2026-02-18 | Initial PoC vision + architecture |
| v2 | 2026-02-18 | Corrected .NET orchestration (Handoff, not Magentic), added Reviewer, risk model, project state |
| v3 | 2026-02-18 | Added Security Builder, Data Population Agent, Tester. Added error recovery, undo, session persistence. Deep Dataverse domain knowledge. Consolidated master spec. |
| v3.1 | 2026-02-18 | Added Business Analyst agent (10 agents total). Differentiated model strategy per agent (GPT-4o for 3, Configurable for 7). Updated handoff topology. |
| v4 | 2026-02-19 | **Major UI/frontend update.** Merged v3.1 addendum into master. Added: (1) Complete design system — NeuralForge dark aesthetic with solid surfaces, dual dark/light theme tokens, Geist + JetBrains Mono typography, agent identity system with colored badges. (2) 4-panel layout — collapsible sidebar (252px → 52px), workspace, collapsible chat panel (348px → hidden with floating FAB), toggleable diagnostics panel (380px). (3) Diagnostics panel spec — real-time tool call capture with agent identity, timestamps, duration pills, collapsible JSON payload/result blocks. (4) Custom SSE integration architecture — ForgeChatProvider with useForgeChat, AG-UI protocol bridge to .NET backend, SSE TOOL_CALL event capture for diagnostics, request_approval server-side tool for approval cards, useAgent for shared state derived from STATE_DELTA events. (5) Agent thinking/reasoning trace — collapsible reasoning blocks in chat showing WHY agents make decisions. (6) Agent Timeline / Gantt view — horizontal visualization of multi-agent orchestration with cross-linking to diagnostics. (7) Token & Cost Tracker — real-time usage accumulation with per-agent breakdown and "manual equivalent" comparison. (8) Voice input added to backlog. Updated MVP roadmap, demo script (now includes timeline showcase and "$0.47" mic-drop moment), and file structure. |