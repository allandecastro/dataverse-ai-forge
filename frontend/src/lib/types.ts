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
