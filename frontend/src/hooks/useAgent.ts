"use client";

import { useForgeChatContext } from "@/hooks/useForgeChat";

export interface SolutionTableField {
  logicalName: string;
  displayName: string;
  fieldType: string;
}

export interface SolutionTable {
  logicalName: string;
  displayName: string;
  fields: SolutionTableField[];
}

export interface SolutionRelationship {
  schemaName: string;
  primaryTable: string;
  relatedTable: string;
  relationshipType: string;
}

type AgentPhase =
  | "idle"
  | "design"
  | "gathering"
  | "building"
  | "reviewing"
  | "testing"
  | "documenting";

const agentToPhase: Record<string, AgentPhase> = {
  SolutionArchitect: "design",
  BusinessAnalyst: "gathering",
  DataModelBuilder: "building",
  Reviewer: "reviewing",
  Tester: "testing",
  Documentor: "documenting",
};

export interface UseAgentReturn {
  currentAgent: string;
  phase: AgentPhase;
  tables: SolutionTable[];
  relationships: SolutionRelationship[];
  solutionName: string;
  solutionPrefix: string;
  solutionVersion: string;
  running: boolean;
}

export function useAgent(): UseAgentReturn {
  const { agentState, isStreaming } = useForgeChatContext();

  const currentAgent = agentState.activeAgent ?? "SolutionArchitect";
  const phase = agentToPhase[currentAgent] ?? "idle";
  const ss = agentState.solutionState;

  return {
    currentAgent,
    phase,
    tables: (ss?.tables as SolutionTable[]) ?? [],
    relationships: (ss?.relationships as SolutionRelationship[]) ?? [],
    solutionName: ss?.solutionName ?? "",
    solutionPrefix: ss?.solutionPrefix ?? "",
    solutionVersion: ss?.solutionVersion ?? "",
    running: isStreaming,
  };
}
