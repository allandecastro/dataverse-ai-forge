"use client";

import { useCoAgent } from "@copilotkit/react-core";

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

export interface AgentState {
  activeAgent: string;
  reasoning?: string;
  solutionName?: string;
  solutionPrefix?: string;
  solutionVersion?: string;
  tables?: SolutionTable[];
  relationships?: SolutionRelationship[];
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
  const { state, running } = useCoAgent<AgentState>({
    name: "dataverse_forge",
    initialState: {
      activeAgent: "SolutionArchitect",
    },
  });

  const currentAgent = state?.activeAgent ?? "SolutionArchitect";
  const phase = agentToPhase[currentAgent] ?? "idle";

  return {
    currentAgent,
    phase,
    tables: state?.tables ?? [],
    relationships: state?.relationships ?? [],
    solutionName: state?.solutionName ?? "",
    solutionPrefix: state?.solutionPrefix ?? "",
    solutionVersion: state?.solutionVersion ?? "",
    running,
  };
}
