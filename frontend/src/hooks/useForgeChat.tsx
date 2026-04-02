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
import type {
  ForgeMessage,
  ToolCallInfo,
  ApprovalRequest,
  AgentState,
  UsageInfo,
} from "@/lib/sse-types";

// ── Context value ──

export interface ForgeChatContextValue {
  messages: ForgeMessage[];
  sendMessage: (text: string) => Promise<void>;
  isStreaming: boolean;
  agentState: AgentState;
  toolCalls: ToolCallInfo[];
  pendingApproval: ApprovalRequest | null;
  clearApproval: () => void;
  lastUsage: UsageInfo | null;
}

const ForgeChatContext = createContext<ForgeChatContextValue>({
  messages: [],
  sendMessage: async () => {},
  isStreaming: false,
  agentState: { activeAgent: "SolutionArchitect" },
  toolCalls: [],
  pendingApproval: null,
  clearApproval: () => {},
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
    const stored = localStorage.getItem(`forge-messages-${sessionId}`);
    if (stored) return JSON.parse(stored) as ForgeMessage[];
  } catch { /* ignore */ }
  return [];
}

function saveMessages(sessionId: string, messages: ForgeMessage[]) {
  if (!sessionId) return;
  try {
    // Keep last 200 messages to avoid localStorage bloat
    const trimmed = messages.slice(-200);
    localStorage.setItem(`forge-messages-${sessionId}`, JSON.stringify(trimmed));
  } catch { /* ignore quota errors */ }
}

export function ForgeChatProvider({ sessionId, children }: ForgeChatProviderProps) {
  const [messages, setMessages] = useState<ForgeMessage[]>(() => loadMessages(sessionId));
  const [isStreaming, setIsStreaming] = useState(false);
  const [agentState, setAgentState] = useState<AgentState>({
    activeAgent: "SolutionArchitect",
  });
  const [toolCalls, setToolCalls] = useState<ToolCallInfo[]>([]);
  const [pendingApproval, setPendingApproval] = useState<ApprovalRequest | null>(null);
  const [lastUsage, setLastUsage] = useState<UsageInfo | null>(null);

  // Persist messages to localStorage
  useEffect(() => {
    if (messages.length > 0) {
      saveMessages(sessionId, messages);
    }
  }, [messages, sessionId]);

  // Keep refs for values accessed inside sendMessage to avoid stale closures
  const messagesRef = useRef(messages);
  messagesRef.current = messages;
  const agentRef = useRef(agentState.activeAgent);
  agentRef.current = agentState.activeAgent;

  // AbortController ref for cancelling in-flight streams
  const abortRef = useRef<AbortController | null>(null);

  const clearApproval = useCallback(() => setPendingApproval(null), []);

  const sendMessage = useCallback(
    async (text: string) => {
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
      };
      setMessages((prev) => [...prev, userMsg]);

      // Build messages array for backend
      const allMessages = [...messagesRef.current, userMsg];
      const backendMessages = allMessages.map((m) => ({
        role: m.role,
        content: m.content,
      }));

      setIsStreaming(true);

      // Track the streaming assistant message
      const assistantId = crypto.randomUUID();
      let assistantContent = "";
      let currentAgentName = agentRef.current;

      // Track tool calls for this run
      const runToolCalls: Map<string, ToolCallInfo> = new Map();

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

        for await (const event of parseSSEStream(res)) {
          // Check for abort between events
          if (controller.signal.aborted) break;

          switch (event.type) {
            case "STATE_DELTA": {
              for (const patch of event.delta) {
                if (patch.path === "/activeAgent" && typeof patch.value === "string") {
                  currentAgentName = patch.value;
                  setAgentState((prev) => ({ ...prev, activeAgent: patch.value as string }));
                }
                if (patch.path === "/reasoning" && typeof patch.value === "string") {
                  setAgentState((prev) => ({ ...prev, reasoning: patch.value as string }));
                }
                if (patch.path === "/solutionState" && patch.value != null) {
                  setAgentState((prev) => ({
                    ...prev,
                    solutionState: patch.value as AgentState["solutionState"],
                  }));
                }
              }
              break;
            }

            case "TEXT_MESSAGE_CONTENT": {
              assistantContent += event.delta;
              setMessages((prev) => {
                const idx = prev.findIndex((m) => m.id === assistantId);
                const msg: ForgeMessage = {
                  id: assistantId,
                  role: "assistant",
                  content: assistantContent,
                  timestamp: Date.now(),
                  agentName: currentAgentName,
                };
                if (idx >= 0) {
                  const updated = [...prev];
                  updated[idx] = msg;
                  return updated;
                }
                return [...prev, msg];
              });
              break;
            }

            case "TOOL_CALL_START": {
              const tc: ToolCallInfo = {
                id: event.toolCallId,
                name: event.toolCallName,
                args: "",
                status: "running",
                startTime: Date.now(),
                messageId: assistantId,
              };
              runToolCalls.set(event.toolCallId, tc);
              setToolCalls((prev) => [...prev, tc]);
              break;
            }

            case "TOOL_CALL_ARGS": {
              const existing = runToolCalls.get(event.toolCallId);
              if (existing) {
                existing.args += event.delta;
                setToolCalls((prev) =>
                  prev.map((tc) =>
                    tc.id === event.toolCallId ? { ...tc, args: existing.args } : tc
                  )
                );
              }
              break;
            }

            case "TOOL_CALL_RESULT": {
              const tc = runToolCalls.get(event.toolCallId);
              if (tc) {
                tc.result = event.result;
                tc.status = event.result.startsWith("ERROR:") ? "error" : "complete";
                tc.endTime = Date.now();
                setToolCalls((prev) =>
                  prev.map((t) =>
                    t.id === event.toolCallId
                      ? { ...t, result: tc.result, status: tc.status, endTime: tc.endTime }
                      : t
                  )
                );

                // Detect request_approval tool
                if (tc.name === "request_approval" || tc.name === "RequestApprovalAsync") {
                  try {
                    const args = JSON.parse(tc.args);
                    setPendingApproval({
                      toolCallId: tc.id,
                      operationType: args.operationType ?? "",
                      description: args.description ?? "",
                      riskLevel: args.riskLevel ?? "orange",
                      details: args.details ?? "",
                    });
                  } catch {
                    // args may not be valid JSON yet
                  }
                }
              }
              break;
            }

            case "RUN_FINISHED": {
              if (event.metadata?.usage) {
                setLastUsage(event.metadata.usage);
              }
              break;
            }

            // RUN_STARTED, TEXT_MESSAGE_START, TEXT_MESSAGE_END, TOOL_CALL_END
            // are lifecycle markers — no action needed
          }
        }
      } catch (err) {
        // Don't show error for intentional aborts
        if (err instanceof DOMException && err.name === "AbortError") return;

        setMessages((prev) => [
          ...prev,
          {
            id: crypto.randomUUID(),
            role: "assistant",
            content: `Connection error: ${err instanceof Error ? err.message : "Unknown error"}. Is the backend running?`,
            timestamp: Date.now(),
            agentName: "SolutionArchitect",
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

  // Memoize context value to prevent unnecessary consumer re-renders
  const value = useMemo<ForgeChatContextValue>(
    () => ({
      messages,
      sendMessage,
      isStreaming,
      agentState,
      toolCalls,
      pendingApproval,
      clearApproval,
      lastUsage,
    }),
    [messages, sendMessage, isStreaming, agentState, toolCalls, pendingApproval, clearApproval, lastUsage]
  );

  return (
    <ForgeChatContext.Provider value={value}>
      {children}
    </ForgeChatContext.Provider>
  );
}
