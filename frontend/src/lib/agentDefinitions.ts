/**
 * User-facing definitions for each agent — role, capabilities, boundaries, and
 * handoff targets. Sourced from the backend prompt files (e.g. SolutionArchitectPrompt.cs)
 * so what users see in the Agents panel matches how the agents actually behave.
 *
 * Keep these terse — this is reference material the user scans, not a manual.
 * The bullet lists mirror the "STAY IN YOUR LANE" sections in each prompt.
 */
export type AgentStatus = "active" | "unsupported";

export interface AgentDefinition {
  /** Canonical agent name — must match the key in `agents.ts`. */
  name: string;
  /** One-line role description. */
  role: string;
  /** Who hands work TO this agent. */
  receivesFrom: string[];
  /** Who this agent hands work to. Empty for terminal/leaf agents. */
  handsOffTo: string[];
  /** What this agent can do — keep to 4-6 bullets. */
  canDo: string[];
  /** What this agent cannot / will not do — keep to 4-6 bullets. */
  cannotDo: string[];
  /** Active means the agent is wired up and runs. Unsupported means it appears
   * in the spec / agent map but the implementation rejects requests with a
   * pointer to the maker portal (per UNSUPPORTED.md). */
  status: AgentStatus;
  /** Why the agent is unsupported, when applicable. */
  unsupportedNote?: string;
}

export const agentDefinitions: Record<string, AgentDefinition> = {
  SolutionArchitect: {
    name: "SolutionArchitect",
    role: "Routes requests to the right specialist agent and owns solution-level operations.",
    receivesFrom: ["User", "Every other agent at end-of-turn"],
    handsOffTo: ["BusinessAnalyst", "DataModelBuilder", "UIBuilder", "DataPopulation", "Tester", "SecurityBuilder", "AutomationBuilder", "DocumentaryAgent"],
    canDo: [
      "Manage solutions: create, select, update version, export, publish-all",
      "Manage publishers (create + reuse)",
      "List tables / forms / views and inspect environment metadata",
      "Add existing components to the active solution",
      "Undo previous executed operations",
      "Hand off to specialist agents",
    ],
    cannotDo: [
      "Create / modify schema directly (→ Data Model Builder)",
      "Build forms or views (→ UI Builder)",
      "Insert or generate records (→ Data Population)",
      "Run post-execution verification (→ Tester)",
    ],
    status: "active",
  },
  BusinessAnalyst: {
    name: "BusinessAnalyst",
    role: "Helps the user clarify requirements before any building starts.",
    receivesFrom: ["SolutionArchitect"],
    handsOffTo: ["SolutionArchitect", "DocumentaryAgent"],
    canDo: [
      "Ask clarifying questions about the user's goal",
      "Read the live environment (tables, solutions, publishers) for grounding",
      "Summarise requirements back to the Solution Architect",
    ],
    cannotDo: [
      "Create / modify anything in Dataverse",
      "Hand off directly to a builder — must route via the Architect (only the Documentary Agent may be called directly, for knowledge-base lookups)",
    ],
    status: "active",
  },
  DataModelBuilder: {
    name: "DataModelBuilder",
    role: "Builds and modifies tables, fields, relationships, and option sets.",
    receivesFrom: ["SolutionArchitect"],
    handsOffTo: ["SolutionArchitect", "Tester"],
    canDo: [
      "Plan + execute table, field, 1:N / N:N relationship, polymorphic / customer lookups",
      "Plan + execute global option sets, status reasons, multi-select picklists",
      "Plan + execute alternate keys (create / delete / deactivate / reactivate)",
      "Modify field metadata (display name, audit, length)",
      "All write operations gated by `request_approval`",
    ],
    cannotDo: [
      "Forms or views (→ UI Builder)",
      "Record-level data (→ Data Population)",
      "Calculated / rollup / formula columns (not supported anywhere)",
      "Approve its own plans — only the user approves via request_approval",
    ],
    status: "active",
  },
  UIBuilder: {
    name: "UIBuilder",
    role: "Builds main / quick-create / quick-view / card forms, saved views, and charts.",
    receivesFrom: ["SolutionArchitect"],
    handsOffTo: ["SolutionArchitect", "Tester"],
    canDo: [
      "Create / update / delete forms (FormXML) — main, quick-create, quick-view, card",
      "Multi-column / multi-section layouts via structured tabsJson; rich subgrids; control catalog (lookup, webresource, quickform, textarea, numeric, date)",
      "Clone existing forms via CopySystemForm; bind OnLoad/OnSave/OnChange handlers; role-gated DisplayConditions",
      "Create / update / delete saved views (savedqueries)",
      "Create / update / delete charts (org-owned savedqueryvisualization) — column / bar / line / pie / funnel / doughnut / area / multiseries / comparison / 100% stacked",
      "List forms / views / charts on a table",
    ],
    cannotDo: [
      "Tables / fields / relationships (→ Data Model Builder)",
      "Records / data (→ Data Population)",
      "User-owned charts (org-owned savedqueryvisualization only)",
      "Sitemaps, business rules, model-driven app shells (not supported)",
      "Delete a table's default Main form (Dataverse blocks it)",
    ],
    status: "active",
  },
  AutomationBuilder: {
    name: "AutomationBuilder",
    role: "Manages the lifecycle of Dataverse Business Process Flows + Business Rules.",
    receivesFrom: ["SolutionArchitect"],
    handsOffTo: ["SolutionArchitect", "Tester"],
    canDo: [
      "List workflows by category (BPF / Business Rule) and table",
      "Rename or re-describe an existing workflow via plan_update_workflow + execute_update_workflow",
      "Activate / deactivate workflows (idempotent on no-ops)",
      "Delete unmanaged workflows with a safety pre-flight (refuses on activated + managed)",
      "Undo previous activate / deactivate operations via undo_last_operation",
    ],
    cannotDo: [
      "Create new BPFs or Business Rules from scratch — deferred until XAML templates can be validated against a real Dataverse env. Build new workflows in the maker portal designer first, then manage them here",
      "Power Automate cloud flows / modern flows (category=5) — maker portal",
      "Classic workflows (category=0) / custom actions (category=3) — maker portal",
      "Plug-ins / C# code generation — maker portal + Visual Studio",
      "Edit a workflow's XAML / clientdata — only name and description are PATCH-able in v1",
    ],
    status: "active",
  },
  SecurityBuilder: {
    name: "SecurityBuilder",
    role: "Builds and maintains Dataverse security roles + their privilege matrices.",
    receivesFrom: ["SolutionArchitect"],
    handsOffTo: ["SolutionArchitect", "Tester"],
    canDo: [
      "Plan + execute role create with full privilege matrix (Create / Read / Write / Delete / Append / AppendTo / Assign / Share)",
      "Plan + execute role update via privilege DELTA — additions, removals, depth changes, optional rename",
      "Delete a role with a safety pre-flight that refuses if any user or team still holds it",
      "Grant ONE privilege at ONE depth to a SET of roles in a single approval (bulk grant)",
      "Bind a role to a RoleTemplate so Dataverse auto-replicates it into every new business unit",
      "All write operations gated by `request_approval`; create + delete reversible via `undo_last_operation` (recreate gets a fresh GUID + zero privileges)",
    ],
    cannotDo: [
      "Tables / fields / relationships (→ Data Model Builder)",
      "Forms / views / charts / apps (→ UI Builder)",
      "Field-level security profiles + column security (`IsSecured` flag) — out of scope, maker portal",
      "Restore the prior privilege matrix on undo of an update (no pre-edit snapshot in v1)",
      "Edit managed / auto-assigned roles — refuses with a maker-portal pointer",
    ],
    status: "active",
  },
  DataPopulation: {
    name: "DataPopulation",
    role: "Inserts seed / reference rows and generates realistic test data.",
    receivesFrom: ["SolutionArchitect"],
    handsOffTo: ["SolutionArchitect", "Tester"],
    canDo: [
      "Plan + execute reference data (lookup / status tables)",
      "Plan + execute test data with realistic values, tagged `[TEST DATA]`",
      "Read records (`get_records`) to fetch real GUIDs for lookup targets",
      "Count records on any table",
      "Auto-translate lookup writes to the correct `@odata.bind` form",
    ],
    cannotDo: [
      "Tables / fields / relationships (→ Data Model Builder)",
      "Forms or views (→ UI Builder)",
      "Update existing rows in place (insert-only today)",
    ],
    status: "active",
  },
  Tester: {
    name: "Tester",
    role: "Post-execution verifier — checks that what the builders did matches the plan.",
    receivesFrom: ["SolutionArchitect", "DataModelBuilder", "UIBuilder", "DataPopulation", "SecurityBuilder", "AutomationBuilder"],
    handsOffTo: ["SolutionArchitect"],
    canDo: [
      "Verify schema against its planId: tables, fields, global option sets, relationships",
      "Verify UI against its planId: forms, views, charts, dashboards, sitemaps, model-driven apps",
      "Verify security against its planId: security roles, bulk privilege grants, app role assignments",
      "Verify automation against its planId: Business Process Flows + Business Rules",
      "Read records + counts to confirm data population",
      "Run full validation across the project's executed plans",
      "Report PASS / FAIL / PARTIAL with concrete discrepancies",
    ],
    cannotDo: [
      "Create / modify / delete anything",
      "Verify pre-existing tables against defaults (false-FAIL trap)",
      "Skip verification on a positive user reply ('Approved' goes to builders, not the Tester)",
    ],
    status: "active",
  },
  DocumentaryAgent: {
    name: "DocumentaryAgent",
    role: "Manages the project's knowledge base: ingests documents and analyses them against each other.",
    receivesFrom: ["SolutionArchitect", "BusinessAnalyst"],
    handsOffTo: ["SolutionArchitect"],
    canDo: [
      "Ingest documents (files, Git repo, Azure DevOps backlog, solution exports), incrementally",
      "List and delete knowledge-base sources",
      "Search ingested content",
      "Gap analysis: spec requirements missing from the backlog (up to 100 per run)",
      "Conflict detection: where two sources contradict each other",
    ],
    cannotDo: [
      "See documents ingested in another project — the knowledge base is per project",
      "Design the data model or modify Dataverse",
      "Elicit requirements (→ Business Analyst)",
      "Stay active after its task — always returns to the Architect",
    ],
    status: "active",
  },
  Documentor: {
    name: "Documentor",
    role: "Was scoped for data dictionary, ERD, and changelog generation.",
    receivesFrom: [],
    handsOffTo: [],
    canDo: [],
    cannotDo: [
      "Anything — this agent is not wired up in the current build",
    ],
    status: "unsupported",
    unsupportedNote:
      "Documentation generation is out of scope; reach for the maker portal's solution component report or roll your own ERD viewer.",
  },
};
