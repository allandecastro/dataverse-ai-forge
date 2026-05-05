import type { DiagnosticEntry, TokenTrackerState, TokenUsage } from "@/lib/types";

// ---------------------------------------------------------------------------
// Chat messages
// ---------------------------------------------------------------------------

export interface DemoChatMessage {
  id: string;
  role: "user" | "assistant";
  agentName?: string;
  content: string;
  thinking?: string;
  isSrs?: boolean;
  approval?: {
    operationType: string;
    description: string;
    riskLevel: "green" | "yellow" | "orange" | "red";
    details: string;
    status: "approved";
  };
}

export const demoChatMessages: DemoChatMessage[] = [
  {
    id: "m-01",
    role: "user",
    content:
      "I need a feedback management system for Contoso. Customers should be able to submit feedback with a rating and category, and our team needs to respond to each piece of feedback.",
  },
  {
    id: "m-02",
    role: "assistant",
    agentName: "SolutionArchitect",
    content:
      "I understand you need a feedback management solution. Let me route you to our Business Analyst to gather detailed requirements before we design the schema.",
    thinking:
      "User wants a feedback system with ratings, categories, and response tracking. This needs BA elicitation before schema design. Handing off to BusinessAnalyst.",
  },
  {
    id: "m-03",
    role: "assistant",
    agentName: "BusinessAnalyst",
    content:
      "Hi! I'd love to help define the requirements for your feedback system. A few questions:\n\n1. What categories should feedback be grouped into? (e.g., Product, Service, Support)\n2. What rating scale do you prefer? (1-5 stars, 1-10, thumbs up/down)\n3. Should feedback responses be tracked with timestamps and responder info?\n4. Are there any status workflows? (e.g., New → In Review → Resolved)",
  },
  {
    id: "m-04",
    role: "user",
    content:
      "Categories: Product, Service, Support, General. Rating 1-5. Yes track responses with who responded. Status flow: New → Under Review → Responded → Closed.",
  },
  {
    id: "m-05",
    role: "assistant",
    agentName: "BusinessAnalyst",
    content:
      "## Requirement Specification\n## Overview\nContoso Feedback Management — a system for collecting, categorizing, and responding to customer feedback with full lifecycle tracking.\n## Entities\n- Feedback (ctso_feedback): Core entity storing each feedback submission with title, description, rating (1-5), category (Product/Service/Support/General), status (New/Under Review/Responded/Closed), submitter lookup, and timestamps.\n- FeedbackResponse (ctso_feedbackresponse): Child entity linked to Feedback via lookup. Stores response body and responder information.\n## Business Rules\n- Default status on new feedback: \"New\"\n- Rating must be between 1 and 5\n- At least one response required before status can be set to \"Responded\"\n## Relationships\n- Feedback 1:N FeedbackResponse (one feedback can have many responses)\n- Feedback N:1 SystemUser (submitted by)\n## Security\n- Feedback Submitter role: Create/Read own feedback, Read responses\n- Feedback Administrator role: Full CRUD on all feedback and responses",
    thinking:
      "User provided clear answers. Structuring into SRS format with entities, rules, relationships, and security. All requirements captured.",
    isSrs: true,
  },
  {
    id: "m-06",
    role: "user",
    content: "Looks good! Please proceed with the design.",
  },
  {
    id: "m-07",
    role: "assistant",
    agentName: "SolutionArchitect",
    content:
      'Great requirements! I\'ll set up the "Contoso Feedback" solution with prefix "ctso" and hand off to the Data Model Builder to create the schema.',
    thinking:
      "BA specs are complete. Creating solution metadata and handing off to DataModelBuilder for schema creation.",
  },
  {
    id: "m-09",
    role: "assistant",
    agentName: "DataModelBuilder",
    content: "I've prepared the table creation plan for your approval.",
    approval: {
      operationType: "CreateTable",
      description:
        'Create table "ctso_feedback" (Feedback) with 8 columns including title, description, rating, category choice, status choice, and submitter lookup.',
      riskLevel: "orange",
      details: JSON.stringify(
        {
          table: "ctso_feedback",
          displayName: "Feedback",
          fields: [
            "ctso_title (Text)",
            "ctso_description (Multiline)",
            "ctso_rating (WholeNumber 1-5)",
            "ctso_category (Choice: Product/Service/Support/General)",
            "ctso_status (Choice: New/Under Review/Responded/Closed)",
            "ctso_submittedby (Lookup → systemuser)",
            "ctso_createdon (DateTime)",
          ],
        },
        null,
        2,
      ),
      status: "approved",
    },
  },
  {
    id: "m-10",
    role: "assistant",
    agentName: "DataModelBuilder",
    content:
      'Table "ctso_feedback" created successfully with all 8 fields. Now creating the response table...',
    thinking:
      "Table creation succeeded. OData returned 204 with EntityId header. Adding to solution component. Moving to ctso_feedbackresponse next.",
  },
  {
    id: "m-11",
    role: "assistant",
    agentName: "DataModelBuilder",
    content:
      'Table "ctso_feedbackresponse" created with 3 fields. Both tables and the 1:N relationship are now in place.',
  },
  {
    id: "m-12",
    role: "assistant",
    agentName: "Tester",
    content:
      "Verification complete:\n\n- ctso_feedback: PASS (8/8 fields verified, all types match)\n- ctso_feedbackresponse: PASS (3/3 fields verified)\n- Relationship ctso_feedback_responses: PASS (1:N, cascade delete = RemoveLink)\n- Lookup ctso_submittedby: PASS (N:1 to systemuser)\n\nAll components verified successfully.",
    thinking:
      "Re-reading all created components from Dataverse API. Comparing expected vs actual metadata. All fields, types, and relationships match the plan.",
  },
  {
    id: "m-13",
    role: "assistant",
    agentName: "Documentor",
    content:
      "Documentation generated:\n- Data dictionary with all tables, fields, and relationships\n- Changelog entry for solution v1.0.0.0\n- ERD diagram (Mermaid format)\n\nAll docs are ready for export.",
  },
  {
    id: "m-14",
    role: "assistant",
    agentName: "SolutionArchitect",
    content:
      "Your Contoso Feedback solution is complete! Here's a summary:\n\n- 2 tables created (Feedback + Feedback Response)\n- 11 fields total across both tables\n- 2 relationships configured\n- All components verified by Tester\n- Documentation generated\n\nThe solution is ready for UI building, automation rules, and security configuration. What would you like to do next?",
  },
];

// ---------------------------------------------------------------------------
// Diagnostic entries
// ---------------------------------------------------------------------------

function buildDiagEntries(): DiagnosticEntry[] {
  // Use a fixed base timestamp to avoid hydration mismatch (Date.now() differs server vs client)
  const base = 1743534000000; // 2025-04-01T18:00:00Z — stable across SSR and client
  let t = base;

  function entry(
    agent: string,
    toolName: string,
    durationMs: number,
    payload: unknown,
    result: unknown,
  ): DiagnosticEntry {
    const e: DiagnosticEntry = {
      id: `diag-${t}`,
      agent,
      toolName,
      timestamp: t,
      duration: durationMs,
      payload,
      result,
      status: "complete",
    };
    t += durationMs + 200; // gap between entries
    return e;
  }

  return [
    // SA initial routing
    entry("SolutionArchitect", "handoff_to_business_analyst", 42, { reason: "gather requirements" }, { success: true }),

    // BA elicitation
    entry("BusinessAnalyst", "elicit_requirements", 1800, { topic: "feedback system" }, { questionsAsked: 4 }),
    entry("BusinessAnalyst", "structure_srs", 2200, { entities: 2 }, { sections: 6, wordCount: 180 }),
    entry("BusinessAnalyst", "handoff_to_solution_architect", 38, { reason: "specs complete" }, { success: true }),

    // SA schema setup
    entry("SolutionArchitect", "validate_schema_name", 120, { name: "ctso_feedback" }, { valid: true, prefix: "ctso" }),
    entry("SolutionArchitect", "validate_schema_name", 115, { name: "ctso_feedbackresponse" }, { valid: true, prefix: "ctso" }),
    entry("SolutionArchitect", "check_oob_table_coverage", 340, { keywords: ["feedback"] }, { oobMatch: null, recommendation: "custom table" }),

    // SA → DMB
    entry("SolutionArchitect", "handoff_to_data_model_builder", 44, { reason: "create tables" }, { success: true }),

    // DMB plan + execute feedback table
    entry("DataModelBuilder", "plan_create_table", 450, { table: "ctso_feedback", fields: 8 }, { risk: "orange", odata: "{...}" }),
    entry("DataModelBuilder", "execute_create_table", 1400, { table: "ctso_feedback" }, { success: true, entityId: "a1b2c3d4" }),
    entry("DataModelBuilder", "execute_create_field", 320, { field: "ctso_title", type: "Text" }, { success: true }),
    entry("DataModelBuilder", "execute_create_field", 310, { field: "ctso_description", type: "Multiline" }, { success: true }),
    entry("DataModelBuilder", "execute_create_field", 290, { field: "ctso_rating", type: "WholeNumber" }, { success: true }),
    entry("DataModelBuilder", "execute_create_field", 340, { field: "ctso_category", type: "Choice" }, { success: true }),
    entry("DataModelBuilder", "execute_create_field", 330, { field: "ctso_status", type: "Choice" }, { success: true }),
    entry("DataModelBuilder", "execute_create_field", 380, { field: "ctso_submittedby", type: "Lookup" }, { success: true }),

    // DMB plan + execute response table
    entry("DataModelBuilder", "plan_create_table", 420, { table: "ctso_feedbackresponse", fields: 3 }, { risk: "orange", odata: "{...}" }),
    entry("DataModelBuilder", "execute_create_table", 1350, { table: "ctso_feedbackresponse" }, { success: true, entityId: "e5f6g7h8" }),
    entry("DataModelBuilder", "execute_create_field", 300, { field: "ctso_feedbackid", type: "Lookup" }, { success: true }),
    entry("DataModelBuilder", "execute_create_field", 310, { field: "ctso_responsebody", type: "Multiline" }, { success: true }),

    // DMB relationship
    entry("DataModelBuilder", "plan_create_one_to_many_relationship", 280, { primary: "ctso_feedback", related: "ctso_feedbackresponse" }, { cascade: "RemoveLink" }),
    entry("DataModelBuilder", "execute_create_one_to_many_relationship", 1200, { schema: "ctso_feedback_responses" }, { success: true }),

    // Handoff to Tester
    entry("DataModelBuilder", "handoff_to_tester", 36, { reason: "creation complete" }, { success: true }),

    // Tester verification
    entry("Tester", "verify_table_creation", 380, { table: "ctso_feedback" }, { result: "PASS", fieldsVerified: 8 }),
    entry("Tester", "verify_table_creation", 350, { table: "ctso_feedbackresponse" }, { result: "PASS", fieldsVerified: 3 }),
    entry("Tester", "verify_relationship_creation", 320, { schema: "ctso_feedback_responses" }, { result: "PASS", cascadeDelete: "RemoveLink" }),
    entry("Tester", "handoff_to_solution_architect", 38, { reason: "all verified" }, { success: true }),

    // SA → Documentor
    entry("SolutionArchitect", "handoff_to_documentor", 40, { reason: "generate docs" }, { success: true }),

    // Documentor
    entry("Documentor", "generate_data_dictionary", 1800, { tables: 2 }, { markdown: "...", wordCount: 420 }),
    entry("Documentor", "generate_changelog_entry", 900, { version: "1.0.0.0" }, { markdown: "..." }),
    entry("Documentor", "generate_erd_doc", 600, { tables: 2, relationships: 2 }, { mermaidLength: 340 }),
    entry("Documentor", "handoff_to_solution_architect", 35, { reason: "docs complete" }, { success: true }),
  ];
}

export const demoDiagnosticEntries: DiagnosticEntry[] = buildDiagEntries();

// ---------------------------------------------------------------------------
// Token state
// ---------------------------------------------------------------------------

function buildTokenState(): TokenTrackerState {
  // Realistic per-agent usage matching the demo conversation
  const usageData: Array<{ agent: string; input: number; output: number; calls: number }> = [
    { agent: "SolutionArchitect", input: 12000, output: 4200, calls: 6 },
    { agent: "BusinessAnalyst", input: 8500, output: 3800, calls: 3 },
    { agent: "DataModelBuilder", input: 6200, output: 2100, calls: 12 },
    { agent: "Tester", input: 3200, output: 1100, calls: 4 },
    { agent: "Documentor", input: 2800, output: 2400, calls: 3 },
    { agent: "AutomationBuilder", input: 1200, output: 400, calls: 1 },
  ];

  const entries: TokenUsage[] = [];
  let totalTokens = 0;

  for (const u of usageData) {
    const perCallInput = Math.round(u.input / u.calls);
    const perCallOutput = Math.round(u.output / u.calls);
    for (let i = 0; i < u.calls; i++) {
      const t = perCallInput + perCallOutput;
      entries.push({
        agentName: u.agent,
        model: u.agent,
        inputTokens: perCallInput,
        outputTokens: perCallOutput,
        totalTokens: t,
      });
      totalTokens += t;
    }
  }

  return { entries, totalTokens };
}

export const demoTokenState: TokenTrackerState = buildTokenState();

// ---------------------------------------------------------------------------
// Solution metadata
// ---------------------------------------------------------------------------

export const demoSolutionMeta = {
  name: "Contoso Feedback",
  prefix: "ctso",
  version: "1.0.0.0",
} as const;
