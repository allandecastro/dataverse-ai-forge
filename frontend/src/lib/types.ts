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
