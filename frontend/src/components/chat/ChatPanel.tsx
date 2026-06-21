"use client";

import { useRef, useEffect, useState, useCallback, useMemo } from "react";
import { Sparkles, Bug, Send, Square, Download, Share2, Trash2 } from "lucide-react";
import { IconBtn } from "@/components/primitives/IconBtn";
import { AgentDot } from "@/components/primitives/AgentDot";
import { StreamingDots } from "@/components/primitives/StreamingDots";
import { ConfirmDialog } from "@/components/primitives/ConfirmDialog";
import { EmptyChat } from "./EmptyChat";
import { MessageRenderer } from "./MessageRenderer";
import { SuggestedReplies } from "./SuggestedReplies";
import { useForgeChatContext } from "@/hooks/useForgeChat";
import { attributeToolCalls } from "@/lib/messageReducer";
import { safeParse } from "@/lib/parsing";
import { useToast } from "@/contexts/ToastContext";
import { agents, defaultAgent } from "@/lib/agents";
import type { ForgeMessage, ToolCallInfo, AgentState } from "@/lib/sse-types";

function exportChat(messages: ForgeMessage[]) {
  const lines = messages.map((m) => {
    const time = new Date(m.timestamp).toLocaleTimeString("en-US", { hour12: false });
    const role = m.role === "user" ? "You" : (agents[m.agentName ?? ""] ?? defaultAgent).displayName;
    return `**${role}** (${time})\n${m.content}\n`;
  });
  const md = `# Dataverse AI Forge — Conversation Export\n\n${lines.join("\n---\n\n")}`;
  const blob = new Blob([md], { type: "text/markdown" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `forge-conversation-${new Date().toISOString().slice(0, 10)}.md`;
  a.click();
  URL.revokeObjectURL(url);
}

/**
 * Build a complete session snapshot for sharing/debugging.
 * Includes messages, tool calls (with args/results), and agent state.
 * Secrets are NOT included — agent state only contains solution metadata.
 */
function buildSessionSnapshot(
  messages: ForgeMessage[],
  toolCalls: ToolCallInfo[],
  agentState: AgentState,
) {
  return {
    exportedAt: new Date().toISOString(),
    activeAgent: agentState.activeAgent,
    solutionState: agentState.solutionState,
    messages: messages.map((m) => ({
      id: m.id,
      role: m.role,
      timestamp: new Date(m.timestamp).toISOString(),
      agentName: m.agentName,
      content: m.content,
    })),
    toolCalls: toolCalls.map((tc) => ({
      id: tc.id,
      name: tc.name,
      messageId: tc.messageId,
      status: tc.status,
      duration: tc.endTime ? tc.endTime - tc.startTime : null,
      args: safeParse(tc.args),
      result: safeParse(tc.result ?? ""),
    })),
  };
}

// Stable reference so messages without tool calls hit MessageRenderer's memo skip path
// (a fresh `[]` literal each render would force a re-render every time).
const NO_TOOL_CALLS: ToolCallInfo[] = [];

async function shareSession(
  messages: ForgeMessage[],
  toolCalls: ToolCallInfo[],
  agentState: AgentState,
) {
  const snapshot = buildSessionSnapshot(messages, toolCalls, agentState);
  const json = JSON.stringify(snapshot, null, 2);

  // Try clipboard first; fall back to download
  try {
    await navigator.clipboard.writeText(json);
    return "clipboard";
  } catch {
    const blob = new Blob([json], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `forge-session-${new Date().toISOString().slice(0, 19).replace(/:/g, "-")}.json`;
    a.click();
    URL.revokeObjectURL(url);
    return "download";
  }
}

interface ChatPanelProps {
  onToggleDiag: () => void;
  diagOpen: boolean;
  currentAgent?: string;
}

export function ChatPanel({
  onToggleDiag,
  diagOpen,
  currentAgent,
}: ChatPanelProps) {
  const { messages, sendMessage, retryLastMessage, stopStreaming, isStreaming, toolCalls, agentState, clearSession } = useForgeChatContext();
  const { addToast } = useToast();
  const [confirmClearOpen, setConfirmClearOpen] = useState(false);

  // Group tool calls by messageId for inline display. Pure logic in lib/messageReducer.ts
  // (unit-tested). See comments there for the attribution strategy.
  const toolCallsByMessage = useMemo(
    () => attributeToolCalls(messages, toolCalls),
    [toolCalls, messages],
  );
  const [input, setInput] = useState("");
  const scrollRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages]);

  const handleSend = useCallback(async () => {
    const text = input.trim();
    if (!text || isStreaming) return;
    setInput("");
    if (textareaRef.current) textareaRef.current.style.height = "auto";
    await sendMessage(text);
  }, [input, isStreaming, sendMessage]);

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        handleSend();
      }
    },
    [handleSend]
  );

  return (
    <section className="flex-1 min-w-0 bg-bg-1 flex flex-col">
      {/* Header */}
      <div className="flex h-12 items-center justify-between border-b border-border px-4">
        <div className="flex items-center gap-2">
          <Sparkles size={14} className="text-accent-ink" />
          <span className="text-t-1 text-sm font-semibold">Studio</span>
          {currentAgent && (
            <div className="flex items-center gap-1.5 px-2 py-0.5 border border-border bg-bg-2">
              <AgentDot agentName={currentAgent} size={14} />
              <span className="text-t-3 text-xs">
                {(agents[currentAgent] ?? defaultAgent).displayName}
              </span>
            </div>
          )}
        </div>
        <div className="flex items-center gap-1">
          <IconBtn
            icon={Share2}
            onClick={async () => {
              const result = await shareSession(messages, toolCalls, agentState);
              addToast(
                result === "clipboard"
                  ? "Session copied to clipboard — paste it to share"
                  : "Session downloaded as JSON",
                "success",
              );
            }}
            title="Copy full session as JSON (for analysis/debugging)"
          />
          <IconBtn
            icon={Download}
            onClick={() => exportChat(messages)}
            title="Export conversation as Markdown"
          />
          <IconBtn
            icon={Trash2}
            onClick={() => setConfirmClearOpen(true)}
            title="Clear session (keeps credentials)"
          />
          <IconBtn
            icon={Bug}
            onClick={onToggleDiag}
            active={diagOpen}
            title="Toggle diagnostics"
          />
        </div>
      </div>

      {/* Messages */}
      <div ref={scrollRef} className="flex-1 overflow-y-auto px-4 py-3 space-y-4">
        {messages.length === 0 && (
          <EmptyChat onPromptClick={sendMessage} disabled={isStreaming} />
        )}
        {messages.map((msg) => (
          <MessageRenderer
            key={msg.id}
            message={msg}
            agentName={msg.agentName ?? currentAgent}
            toolCalls={toolCallsByMessage.get(msg.id) ?? NO_TOOL_CALLS}
            onRetry={msg.isError ? retryLastMessage : undefined}
            retryDisabled={isStreaming}
          />
        ))}
        {/* Suggested-reply chips surfaced by the agent's `suggest_replies` tool. */}
        {!isStreaming && (
          <SuggestedReplies
            suggestions={agentState.suggestedReplies}
            disabled={isStreaming}
            onPick={(text) => void sendMessage(text)}
          />
        )}
        {isStreaming && messages[messages.length - 1]?.role !== "assistant" && (
          <div className="flex gap-2 items-center text-t-3 text-xs">
            <StreamingDots />
            <span>Thinking...</span>
          </div>
        )}
      </div>

      {/* Input */}
      <div className="border-t border-border px-4 py-3">
        <div className="flex items-end gap-2 border border-border-hi bg-bg-1 px-3 py-2.5 max-w-3xl mx-auto transition-colors focus-within:border-accent-ink">
          <textarea
            ref={textareaRef}
            value={input}
            onChange={(e) => {
              setInput(e.target.value);
              // Auto-resize
              e.target.style.height = "auto";
              e.target.style.height = Math.min(e.target.scrollHeight, 160) + "px";
            }}
            onKeyDown={handleKeyDown}
            placeholder="Ask about your Dataverse solution..."
            className="flex-1 bg-transparent text-sm text-t-1 placeholder:text-t-4 outline-none resize-none"
            rows={1}
            style={{ maxHeight: 160 }}
          />
          {isStreaming ? (
            <button
              onClick={stopStreaming}
              className="flex items-center justify-center p-1.5 bg-red text-white transition-colors flex-shrink-0 mb-0.5 hover:bg-red/80"
              title="Stop the agent"
            >
              <Square size={14} fill="currentColor" />
            </button>
          ) : (
            <button
              onClick={handleSend}
              disabled={!input.trim()}
              className="flex items-center justify-center p-1.5 transition-colors flex-shrink-0 mb-0.5 text-accent-fg disabled:text-t-4 disabled:bg-bg-3"
              style={{ background: input.trim() ? "var(--accent)" : undefined }}
              title="Send message"
            >
              <Send size={14} />
            </button>
          )}
        </div>
      </div>

      <ConfirmDialog
        open={confirmClearOpen}
        title="Clear session?"
        message={
          "This will delete all messages and tool call history for this session.\n" +
          "Your credentials and project settings will be kept."
        }
        confirmLabel="Clear"
        destructive
        onConfirm={() => {
          clearSession();
          setConfirmClearOpen(false);
          addToast("Session cleared", "success");
        }}
        onCancel={() => setConfirmClearOpen(false)}
      />
    </section>
  );
}
