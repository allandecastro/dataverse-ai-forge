"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { parseSSEStream } from "@/lib/sse-parser";
import { dropEmptyTrailingBubble } from "@/lib/messageReducer";
import { initStreamState, streamReducer, PLACEHOLDER_PREFIX, type StreamState } from "@/lib/streamReducer";
import { DEFAULT_AGENT_NAME, STORAGE_CHAT_MESSAGES, MESSAGES_PERSIST_LIMIT } from "@/lib/constants";
import type {
  ForgeMessage,
  MessageKind,
  ToolCallInfo,
  ApprovalRequest,
  AgentState,
  UsageInfo,
} from "@/lib/sse-types";

// ── Context value ──

export interface SendMessageOptions {
  /** Tag the user message with a render hint (chip vs. standard blue bubble). */
  kind?: MessageKind;
}

export interface ForgeChatContextValue {
  messages: ForgeMessage[];
  sendMessage: (text: string, options?: SendMessageOptions) => Promise<void>;
  /** Re-send the most recent user message. Pops the trailing error bubble first
   * so the user doesn't end up with a stack of failed-attempts in their chat. */
  retryLastMessage: () => Promise<void>;
  /** Abort the in-flight stream. No-op if nothing is streaming. The current
   * partial assistant bubble is preserved (the user sees what arrived so far). */
  stopStreaming: () => void;
  isStreaming: boolean;
  agentState: AgentState;
  toolCalls: ToolCallInfo[];
  pendingApproval: ApprovalRequest | null;
  clearApproval: () => void;
  clearSession: () => void;
  lastUsage: UsageInfo | null;
}

const ForgeChatContext = createContext<ForgeChatContextValue>({
  messages: [],
  sendMessage: async () => {},
  retryLastMessage: async () => {},
  stopStreaming: () => {},
  isStreaming: false,
  agentState: { activeAgent: DEFAULT_AGENT_NAME },
  toolCalls: [],
  pendingApproval: null,
  clearApproval: () => {},
  clearSession: () => {},
  lastUsage: null,
});

export function useForgeChatContext() {
  return useContext(ForgeChatContext);
}

// ── Provider ──

interface ForgeChatProviderProps {
  sessionId: string;
  children: ReactNode;
}

function loadMessages(sessionId: string): ForgeMessage[] {
  if (typeof window === "undefined" || !sessionId) return [];
  try {
    const stored = localStorage.getItem(STORAGE_CHAT_MESSAGES(sessionId));
    if (stored) {
      const parsed = JSON.parse(stored) as ForgeMessage[];
      // Drop placeholder bubbles persisted mid-stream — their tool calls
      // aren't persisted, so on reload they'd render as empty agent rows.
      return parsed.filter((m) => !m.id.startsWith(PLACEHOLDER_PREFIX));
    }
  } catch { /* ignore */ }
  return [];
}

function saveMessages(sessionId: string, messages: ForgeMessage[]) {
  if (!sessionId) return;
  try {
    const trimmed = messages.slice(-MESSAGES_PERSIST_LIMIT);
    localStorage.setItem(STORAGE_CHAT_MESSAGES(sessionId), JSON.stringify(trimmed));
  } catch { /* ignore quota errors */ }
}

export function ForgeChatProvider({ sessionId, children }: ForgeChatProviderProps) {
  const [messages, setMessages] = useState<ForgeMessage[]>(() => loadMessages(sessionId));
  const [isStreaming, setIsStreaming] = useState(false);
  const [agentState, setAgentState] = useState<AgentState>({
    activeAgent: DEFAULT_AGENT_NAME,
  });
  const [toolCalls, setToolCalls] = useState<ToolCallInfo[]>([]);
  const [pendingApproval, setPendingApproval] = useState<ApprovalRequest | null>(null);
  const [lastUsage, setLastUsage] = useState<UsageInfo | null>(null);

  // Persist messages to localStorage — but NOT on every streamed token.
  // The reducer returns a fresh `messages` array on each TEXT_MESSAGE_CONTENT
  // chunk, so persisting unconditionally meant a full JSON.stringify + a
  // synchronous localStorage.setItem *per token* (hundreds of writes for one
  // long agent reply, on the main thread). Gate on `!isStreaming`: the effect
  // still fires when the stream ends (isStreaming flips to false) and writes the
  // final state once. The only unpersisted window is mid-stream — a reload there
  // drops the in-flight turn, which loadMessages already discards anyway (it
  // filters placeholder bubbles whose tool calls aren't persisted).
  useEffect(() => {
    if (!isStreaming && messages.length > 0) {
      saveMessages(sessionId, messages);
    }
  }, [messages, isStreaming, sessionId]);

  // Keep refs for values accessed inside sendMessage to avoid stale closures.
  // Without these the streamReducer's seed would use stale React state if the
  // user fired sendMessage faster than React re-rendered the callback.
  const messagesRef = useRef(messages);
  messagesRef.current = messages;
  const agentRef = useRef(agentState.activeAgent);
  agentRef.current = agentState.activeAgent;
  const agentStateRef = useRef(agentState);
  agentStateRef.current = agentState;
  // Without this ref, every new turn seeds streamReducer with toolCalls=[]
  // and the React state collapses to ONLY the current turn's tool calls —
  // earlier agents disappear from Agent Timeline / Activity / Diagnostics.
  const toolCallsRef = useRef(toolCalls);
  toolCallsRef.current = toolCalls;

  // AbortController ref for cancelling in-flight streams
  const abortRef = useRef<AbortController | null>(null);

  const clearApproval = useCallback(() => setPendingApproval(null), []);

  const stopStreaming = useCallback(() => {
    // Aborts the fetch + breaks the for-await loop in sendMessage. The
    // partial assistant bubble is preserved so the user sees what arrived
    // before they hit Stop.
    abortRef.current?.abort();
    abortRef.current = null;
    setIsStreaming(false);
    // Drop a trailing empty assistant bubble if Stop fired after
    // TEXT_MESSAGE_START opened it but before any content arrived.
    setMessages((prev) => dropEmptyTrailingBubble(prev));
  }, []);

  const clearSession = useCallback(() => {
    // Cancel any in-flight stream
    abortRef.current?.abort();
    abortRef.current = null;

    // Clear all in-memory session state (credentials are stored in backend, not touched)
    setMessages([]);
    setToolCalls([]);
    setPendingApproval(null);
    setLastUsage(null);
    setAgentState({ activeAgent: DEFAULT_AGENT_NAME });
    setIsStreaming(false);

    // Clear persisted messages for this session
    if (typeof window !== "undefined" && sessionId) {
      localStorage.removeItem(STORAGE_CHAT_MESSAGES(sessionId));
    }
  }, [sessionId]);

  const sendMessage = useCallback(
    async (text: string, options?: SendMessageOptions) => {
      if (isStreaming) return;

      // Abort any previous request
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      // Add user message
      const userMsg: ForgeMessage = {
        id: crypto.randomUUID(),
        role: "user",
        content: text,
        timestamp: Date.now(),
        kind: options?.kind,
      };
      setMessages((prev) => [...prev, userMsg]);

      // Build messages array for backend
      const allMessages = [...messagesRef.current, userMsg];
      const backendMessages = allMessages.map((m) => ({
        role: m.role,
        content: m.content,
      }));

      setIsStreaming(true);

      // Per-turn streaming state lives in a single object the streamReducer
      // mutates per event. The hook just dispatches into it and writes the
      // result back to React state. Keeps the SSE state machine pure +
      // testable in lib/streamReducer.ts.
      let streamState: StreamState = initStreamState({
        messages: messagesRef.current.concat(userMsg),
        // Seed with EXISTING tool calls so the new turn appends to them — without
        // this the React state collapses to just the current turn's calls and
        // earlier agents (SA/BA) vanish from Agent Timeline / Activity / Diagnostics.
        toolCalls: toolCallsRef.current,
        agentState: agentStateRef.current,
        pendingApproval: null,
        // lastUsage is only WRITTEN by the reducer (on RUN_FINISHED) so a null
        // seed is fine — the change-detection below picks up the new value.
        lastUsage: null,
        initialAgentName: agentRef.current,
      });

      try {
        const res = await fetch("/api/chat", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          signal: controller.signal,
          body: JSON.stringify({
            threadId: sessionId,
            runId: crypto.randomUUID(),
            messages: backendMessages,
          }),
        });

        if (!res.ok) {
          throw new Error(`Backend error: ${res.status}`);
        }

        for await (const event of parseSSEStream(res, controller.signal)) {
          // The parser itself now checks signal.aborted between events, but
          // keep the caller-side guard so the post-event setState burst
          // also short-circuits.
          if (controller.signal.aborted) break;

          const next = streamReducer(streamState, event, Date.now());
          // Push only the slices that changed — direct setX is safe because no
          // other code path mutates these arrays during a stream.
          if (next.messages !== streamState.messages) setMessages(next.messages);
          if (next.toolCalls !== streamState.toolCalls) setToolCalls(next.toolCalls);
          if (next.agentState !== streamState.agentState) setAgentState(next.agentState);
          // Intentionally NOT propagating pendingApproval to React state here —
          // the request_approval tool result often resolves BEFORE the agent
          // finishes streaming its rationale, and popping the card mid-stream
          // truncates the user's read of "why am I being asked to approve this?".
          // We hold the parsed approval inside streamState and flush it once
          // the SSE loop completes (see the post-loop block below).
          if (next.lastUsage !== streamState.lastUsage && next.lastUsage) {
            setLastUsage(next.lastUsage);
          }
          streamState = next;
        }

        // Stream finished cleanly — NOW surface the approval card. Done after
        // the loop so the agent's full bubble has rendered first; the user
        // reads the rationale, then decides on the card.
        if (streamState.pendingApproval) {
          setPendingApproval(streamState.pendingApproval);
        }
      } catch (err) {
        // Don't show error for intentional aborts
        if (err instanceof DOMException && err.name === "AbortError") return;

        // If an approval was parsed before the stream broke, surface it anyway —
        // otherwise it's silently lost and the user never sees the card. Approving
        // resubmits via a fresh sendMessage ("User approved."), so the dead stream
        // doesn't block recovery.
        if (streamState.pendingApproval) {
          setPendingApproval(streamState.pendingApproval);
        }

        setMessages((prev) => [
          ...prev,
          {
            id: crypto.randomUUID(),
            role: "assistant",
            content: `Connection error: ${err instanceof Error ? err.message : "Unknown error"}. Is the backend running?`,
            timestamp: Date.now(),
            agentName: DEFAULT_AGENT_NAME,
            isError: true,
          },
        ]);
      } finally {
        setIsStreaming(false);
        if (abortRef.current === controller) {
          abortRef.current = null;
        }
      }
    },
    [isStreaming, sessionId]
  );

  const retryLastMessage = useCallback(async () => {
    if (isStreaming) return;

    // Find the most recent user message; pop any trailing error bubbles first
    // (so the chat doesn't accumulate "Connection error" messages on each retry).
    const current = messagesRef.current;
    let lastUserIdx = -1;
    for (let i = current.length - 1; i >= 0; i--) {
      if (current[i].role === "user") { lastUserIdx = i; break; }
    }
    if (lastUserIdx === -1) return;

    const lastUserText = current[lastUserIdx].content;
    // Drop the user message itself + everything after — sendMessage will re-append it.
    const truncated = current.slice(0, lastUserIdx);
    // Keep the ref in sync BEFORE calling sendMessage: sendMessage seeds the
    // streamReducer from messagesRef.current synchronously, before React flushes
    // this setMessages. Without this the dropped error bubbles get re-seeded and
    // reappear, defeating the whole point of trimming them here.
    messagesRef.current = truncated;
    setMessages(truncated);
    await sendMessage(lastUserText);
  }, [isStreaming, sendMessage]);

  // Memoize context value to prevent unnecessary consumer re-renders
  const value = useMemo<ForgeChatContextValue>(
    () => ({
      messages,
      sendMessage,
      retryLastMessage,
      stopStreaming,
      isStreaming,
      agentState,
      toolCalls,
      pendingApproval,
      clearApproval,
      clearSession,
      lastUsage,
    }),
    [messages, sendMessage, retryLastMessage, stopStreaming, isStreaming, agentState, toolCalls, pendingApproval, clearApproval, clearSession, lastUsage]
  );

  return (
    <ForgeChatContext.Provider value={value}>
      {children}
    </ForgeChatContext.Provider>
  );
}
