export interface DiagnosticEntry {
  id: string;
  agent: string;
  toolName: string;
  timestamp: number;
  duration: number | null;
  payload: unknown;
  result: unknown;
  status: "running" | "complete" | "error";
}

export type ThemeMode = "dark" | "light";

export type RiskLevel = "green" | "yellow" | "orange" | "red";

export type ApprovalStatus = "pending" | "approved" | "rejected";

export interface TokenUsage {
  agentName: string;
  model: string;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
}

export interface TokenTrackerState {
  entries: TokenUsage[];
  totalTokens: number;
}

/**
 * Shape of the environment summary shown in the sidebar footer (name + URL +
 * "Connected"/"Development"/etc. type label). Lives here rather than in
 * `demo-data.ts` so production UI components don't have to import (even just
 * for the type) from a demo-fixtures module.
 */
export interface DemoEnvironment {
  name: string;
  url: string;
  type: string;
}
