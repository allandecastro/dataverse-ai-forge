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
    handsOffTo: ["BusinessAnalyst", "DataModelBuilder", "UIBuilder", "DataPopulation", "Reviewer", "Tester"],
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
      "Run pre/post-execution review or verification (→ Reviewer / Tester)",
    ],
    status: "active",
  },
  BusinessAnalyst: {
    name: "BusinessAnalyst",
    role: "Helps the user clarify requirements before any building starts.",
    receivesFrom: ["SolutionArchitect"],
    handsOffTo: ["SolutionArchitect"],
    canDo: [
      "Ask clarifying questions about the user's goal",
      "Read the live environment (tables, solutions, publishers) for grounding",
      "Summarise requirements back to the Solution Architect",
    ],
    cannotDo: [
      "Create / modify anything in Dataverse",
      "Hand off directly to a builder — must route via the Architect",
    ],
    status: "active",
  },
  DataModelBuilder: {
    name: "DataModelBuilder",
    role: "Builds and modifies tables, fields, relationships, and option sets.",
    receivesFrom: ["SolutionArchitect"],
    handsOffTo: ["SolutionArchitect", "Reviewer", "Tester"],
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
      "Approve its own plans — Reviewer/User does that",
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
    role: "Was scoped for Power Automate flows + business rules.",
    receivesFrom: [],
    handsOffTo: [],
    canDo: [],
    cannotDo: [
      "Anything — this agent is not wired up in the current build",
    ],
    status: "unsupported",
    unsupportedNote:
      "Cloud flows / business rules / classic workflows are out of scope. Build them in the Power Apps maker portal.",
  },
  SecurityBuilder: {
    name: "SecurityBuilder",
    role: "Was scoped for security roles + field security profiles.",
    receivesFrom: [],
    handsOffTo: [],
    canDo: [],
    cannotDo: [
      "Anything — this agent is not wired up in the current build",
    ],
    status: "unsupported",
    unsupportedNote:
      "Security roles, field-level security, and column security profiles are out of scope. Configure them in the maker portal.",
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
    receivesFrom: ["SolutionArchitect", "DataModelBuilder", "UIBuilder", "DataPopulation"],
    handsOffTo: ["SolutionArchitect"],
    canDo: [
      "Verify table / field / global option set / chart against their planId",
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
  Reviewer: {
    name: "Reviewer",
    role: "Pre-execution quality gate — validates schema designs before they ship.",
    receivesFrom: ["SolutionArchitect", "DataModelBuilder"],
    handsOffTo: ["SolutionArchitect"],
    canDo: [
      "Score schema designs 0–100 with risk level + verdict",
      "Flag Dataverse limit violations and best-practice issues",
      "Refuse to review when the spec is empty / vague (no fabricated findings)",
    ],
    cannotDo: [
      "Create / modify Dataverse — review only",
      "Approve / reject on the user's behalf",
      "Hand off to a builder directly (routes through the Architect)",
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
