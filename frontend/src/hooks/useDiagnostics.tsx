"use client";

import { useRef, useMemo, useEffect, useCallback } from "react";
import { useForgeChatContext } from "@/hooks/useForgeChat";
import type { DiagnosticEntry } from "@/lib/types";
import { DEFAULT_AGENT_NAME } from "@/lib/constants";
import { useToast } from "@/contexts/ToastContext";

// Static map of tool names to agent names
const toolToAgent: Record<string, string> = {
  // Solution Architect tools
  GetEnvironmentInfoAsync: "SolutionArchitect",
  GetSolutionsListAsync: "SolutionArchitect",
  GetPublishersAsync: "SolutionArchitect",
  GetTablesListAsync: "SolutionArchitect",
  GetTableDetailsAsync: "SolutionArchitect",
  ValidateSchemaNameAsync: "SolutionArchitect",
  CheckOobTableCoverageAsync: "SolutionArchitect",
  GenerateErdMermaidAsync: "SolutionArchitect",
  HandoffToBusinessAnalystAsync: "SolutionArchitect",
  HandoffToDataModelBuilderAsync: "SolutionArchitect",
  HandoffToReviewerAsync: "SolutionArchitect",
  HandoffToTesterAsync: "SolutionArchitect",
  HandoffToDocumentorAsync: "SolutionArchitect",
  // Business Analyst tools
  HandoffToSolutionArchitectAsync: "BusinessAnalyst",
  // Data Model Builder tools
  PlanCreateTableAsync: "DataModelBuilder",
  ExecuteCreateTableAsync: "DataModelBuilder",
  PlanCreateFieldAsync: "DataModelBuilder",
  ExecuteCreateFieldAsync: "DataModelBuilder",
  PlanCreateOneToManyRelationshipAsync: "DataModelBuilder",
  ExecuteCreateOneToManyRelationshipAsync: "DataModelBuilder",
  RequestApprovalAsync: "DataModelBuilder",
  // Reviewer tools
  ValidateSchemaDesignAsync: "Reviewer",
  // Tester tools
  VerifyTableCreationAsync: "Tester",
  VerifyFieldCreationAsync: "Tester",
  VerifyRelationshipCreationAsync: "Tester",
  RunFullValidationAsync: "Tester",
  // Documentor tools
  GenerateDataDictionaryAsync: "Documentor",
  GenerateChangelogEntryAsync: "Documentor",
  GenerateErdDocAsync: "Documentor",
  PushDocumentationToGitHubAsync: "Documentor",
};

function resolveAgent(toolName: string): string {
  return toolToAgent[toolName] ?? DEFAULT_AGENT_NAME;
}

function safeJsonParse(str: string): unknown {
  try {
    return JSON.parse(str);
  } catch {
    return str;
  }
}

interface UseDiagnosticsReturn {
  entries: DiagnosticEntry[];
  clearEntries: () => void;
}

export function useDiagnostics(): UseDiagnosticsReturn {
  const { toolCalls } = useForgeChatContext();
  const { addToast } = useToast();
  const toastedRef = useRef<Set<string>>(new Set());

  // Derive diagnostic entries from tool calls (pure derivation)
  const entries = useMemo(() => {
    return toolCalls.map((tc): DiagnosticEntry => {
      const isError = tc.status === "error" || (tc.result ? /^"?error[:\s]/i.test(tc.result) : false);
      const duration = tc.endTime != null ? tc.endTime - tc.startTime : null;
      return {
        id: tc.id,
        // Prefer the active agent recorded by the server at TOOL_CALL_START — that's
        // accurate even for shared tools (Handoff*). Fall back to the static map.
        agent: tc.agent ?? resolveAgent(tc.name),
        toolName: tc.name,
        timestamp: tc.startTime,
        duration,
        payload: tc.args ? safeJsonParse(tc.args) : null,
        result: tc.result ? safeJsonParse(tc.result) : null,
        status: isError ? "error" : tc.status === "complete" ? "complete" : "running",
      };
    });
  }, [toolCalls]);

  // Fire toasts for errors (side effect in useEffect)
  useEffect(() => {
    for (const tc of toolCalls) {
      const isError = tc.status === "error" || (tc.result ? /^"?error[:\s]/i.test(tc.result) : false);
      if (isError && !toastedRef.current.has(tc.id)) {
        toastedRef.current.add(tc.id);
        const msg = `${tc.name} failed: ${(tc.result ?? "").slice(6).trim()}`.slice(0, 120);
        addToast(msg, "error");
      }
    }
  }, [toolCalls, addToast]);

  const clearEntries = useCallback(() => {
    toastedRef.current.clear();
  }, []);

  return { entries, clearEntries };
}
