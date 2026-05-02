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
  agentName?: string;
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

/** Special user-message variants rendered as compact chips instead of the
 * standard blue bubble. Used for approval flow signals (Approve / Reject /
 * Edit) so the chat doesn't get cluttered with synthetic "APPROVED BATCH:..."
 * text. The chip is purely a render hint — the `content` field still carries
 * the short string the LLM sees. */
export type MessageKind =
  | "approval-receipt"
  | "approval-rejection"
  | "approval-edit";

export interface ForgeMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  timestamp: number;
  agentName?: string;
  /** True when this assistant message represents a transport / backend failure
   * the user can retry. Set by useForgeChat when the SSE stream errors out. */
  isError?: boolean;
  /** Render hint for special user messages (approval signals). Undefined =
   * standard rendering (assistant bubble or user bubble depending on role). */
  kind?: MessageKind;
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
  /** Agent that called request_approval (e.g. DataModelBuilder) — used to label the card. */
  agentName?: string;
  // Populated when the backend used `request_batch_approval` instead of `request_approval`.
  // Each entry is one planned operation in the batch; the user's single Approve/Reject
  // decision applies to the whole list.
  batchItems?: Array<{
    planId: string;
    summary: string;
    riskLevel: string;
  }>;
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
  /** Suggested user replies surfaced by the agent's last `suggest_replies` tool
   * call. Rendered as clickable chips under the agent's last bubble. Cleared
   * automatically at the start of each new run, on user typing/sending, and
   * on chip click. */
  suggestedReplies?: string[];
}
