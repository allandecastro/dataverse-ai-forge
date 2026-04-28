// ── SSE Event Types (AG-UI protocol from backend) ──

export interface RunStartedEvent {
  type: "RUN_STARTED";
  threadId: string;
  runId: string;
}

export interface StateDeltaEvent {
  type: "STATE_DELTA";
  threadId: string;
  runId: string;
  delta: Array<{ op: string; path: string; value: unknown }>;
}

export interface TextMessageStartEvent {
  type: "TEXT_MESSAGE_START";
  threadId: string;
  runId: string;
  messageId: string;
  role: string;
}

export interface TextMessageContentEvent {
  type: "TEXT_MESSAGE_CONTENT";
  threadId: string;
  runId: string;
  messageId: string;
  delta: string;
}

export interface TextMessageEndEvent {
  type: "TEXT_MESSAGE_END";
  threadId: string;
  runId: string;
  messageId: string;
}

export interface ToolCallStartEvent {
  type: "TOOL_CALL_START";
  threadId: string;
  runId: string;
  toolCallId: string;
  toolCallName: string;
  agentName?: string;
}

export interface ToolCallArgsEvent {
  type: "TOOL_CALL_ARGS";
  threadId: string;
  runId: string;
  toolCallId: string;
  delta: string;
}

export interface ToolCallEndEvent {
  type: "TOOL_CALL_END";
  threadId: string;
  runId: string;
  toolCallId: string;
}

export interface ToolCallResultEvent {
  type: "TOOL_CALL_RESULT";
  threadId: string;
  runId: string;
  toolCallId: string;
  result: string;
}

export interface RunFinishedEvent {
  type: "RUN_FINISHED";
  threadId: string;
  runId: string;
  metadata?: {
    usage?: UsageInfo;
  };
}

export type SSEEvent =
  | RunStartedEvent
  | StateDeltaEvent
  | TextMessageStartEvent
  | TextMessageContentEvent
  | TextMessageEndEvent
  | ToolCallStartEvent
  | ToolCallArgsEvent
  | ToolCallEndEvent
  | ToolCallResultEvent
  | RunFinishedEvent;

// ── Application Types ──

export interface UsageInfo {
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
}

export interface ForgeMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  timestamp: number;
  agentName?: string;
}

export interface ToolCallInfo {
  id: string;
  name: string;
  args: string;
  result?: string;
  status: "running" | "complete" | "error";
  startTime: number;
  endTime?: number;
  messageId?: string;
  agent?: string;
}

export interface ApprovalRequest {
  toolCallId: string;
  operationType: string;
  description: string;
  riskLevel: string;
  details: string;
}

export interface SolutionStateData {
  environmentName?: string;
  environmentUrl?: string;
  solutionName?: string;
  solutionPrefix?: string;
  solutionVersion?: string;
  tables?: Array<{
    logicalName: string;
    displayName: string;
    fields: Array<{
      logicalName: string;
      displayName: string;
      fieldType: string;
    }>;
  }>;
  relationships?: Array<{
    schemaName: string;
    primaryTable: string;
    relatedTable: string;
    relationshipType: string;
  }>;
}

export interface AgentState {
  activeAgent: string;
  reasoning?: string;
  solutionState?: SolutionStateData;
}
