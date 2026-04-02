"use client";

import { useForgeChatContext } from "@/hooks/useForgeChat";
import { DEFAULT_AGENT_NAME } from "@/lib/constants";

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
  solutionName: string;
  solutionPrefix: string;
  solutionVersion: string;
  running: boolean;
}

export function useAgent(): UseAgentReturn {
  const { agentState, isStreaming } = useForgeChatContext();

  const currentAgent = agentState.activeAgent ?? DEFAULT_AGENT_NAME;
  const phase = agentToPhase[currentAgent] ?? "idle";
  const ss = agentState.solutionState;

  return {
    currentAgent,
    phase,
    solutionName: ss?.solutionName ?? "",
    solutionPrefix: ss?.solutionPrefix ?? "",
    solutionVersion: ss?.solutionVersion ?? "",
    running: isStreaming,
  };
}
