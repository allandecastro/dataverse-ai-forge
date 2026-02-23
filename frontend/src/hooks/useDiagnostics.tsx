"use client";

import { useRef, useCallback, useState } from "react";
import { useCopilotAction } from "@copilotkit/react-core";
import type { DiagnosticEntry } from "@/lib/types";
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
  return toolToAgent[toolName] ?? "SolutionArchitect";
}

interface UseDiagnosticsReturn {
  entries: DiagnosticEntry[];
  clearEntries: () => void;
}

export function useDiagnostics(): UseDiagnosticsReturn {
  const [entries, setEntries] = useState<DiagnosticEntry[]>([]);
  const startTimesRef = useRef<Map<string, number>>(new Map());
  const { addToast } = useToast();

  const clearEntries = useCallback(() => {
    setEntries([]);
    startTimesRef.current.clear();
  }, []);

  useCopilotAction({
    name: "*",
    render: (props: {
      name: string;
      status: string;
      args: Record<string, unknown>;
      result: unknown;
    }) => {
      const { name, status, args, result } = props;
      const entryId = `${name}-${JSON.stringify(args).slice(0, 50)}`;

      if (status === "inProgress" || status === "executing") {
        if (!startTimesRef.current.has(entryId)) {
          startTimesRef.current.set(entryId, Date.now());
          const entry: DiagnosticEntry = {
            id: entryId + "-" + Date.now(),
            agent: resolveAgent(name),
            toolName: name,
            timestamp: Date.now(),
            duration: null,
            payload: args,
            result: null,
            status: "running",
          };
          setTimeout(() => setEntries((prev) => [...prev, entry]), 0);
        }
      }

      if (status === "complete") {
        const startTime = startTimesRef.current.get(entryId);
        const duration = startTime ? Date.now() - startTime : null;
        startTimesRef.current.delete(entryId);

        // Show toast on tool error
        const resultStr = typeof result === "string" ? result : "";
        if (resultStr.startsWith("ERROR:")) {
          const msg = `${name} failed: ${resultStr.slice(6).trim()}`.slice(0, 120);
          addToast(msg, "error");
        }

        setTimeout(
          () =>
            setEntries((prev) => {
              const entryStatus = resultStr.startsWith("ERROR:") ? "error" as const : "complete" as const;
              const idx = prev.findIndex(
                (e) => e.toolName === name && e.status === "running"
              );
              if (idx >= 0) {
                const updated = [...prev];
                updated[idx] = {
                  ...updated[idx],
                  duration,
                  result,
                  status: entryStatus,
                };
                return updated;
              }
              return [
                ...prev,
                {
                  id: entryId + "-" + Date.now(),
                  agent: resolveAgent(name),
                  toolName: name,
                  timestamp: Date.now(),
                  duration,
                  payload: args,
                  result,
                  status: entryStatus,
                },
              ];
            }),
          0
        );
      }

      return <></>;
    },
  });

  return { entries, clearEntries };
}
