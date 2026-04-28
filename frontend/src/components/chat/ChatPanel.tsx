"use client";

import { useRef, useEffect, useState, useCallback, useMemo } from "react";
import { Sparkles, Bug, Send, Download, Share2 } from "lucide-react";
import { IconBtn } from "@/components/primitives/IconBtn";
import { AgentDot } from "@/components/primitives/AgentDot";
import { StreamingDots } from "@/components/primitives/StreamingDots";
import { MessageRenderer } from "./MessageRenderer";
import { useForgeChatContext } from "@/hooks/useForgeChat";
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
      args: tryParseJson(tc.args),
      result: tryParseJson(tc.result ?? ""),
    })),
  };
}

function tryParseJson(s: string): unknown {
  try { return JSON.parse(s); } catch { return s; }
}

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
  const { messages, sendMessage, isStreaming, toolCalls, agentState } = useForgeChatContext();
  const { addToast } = useToast();

  // Group tool calls by messageId for inline display
  const toolCallsByMessage = useMemo(() => {
    const map = new Map<string, typeof toolCalls>();
    for (const tc of toolCalls) {
      if (tc.messageId) {
        const existing = map.get(tc.messageId) ?? [];
        existing.push(tc);
        map.set(tc.messageId, existing);
      }
    }
    return map;
  }, [toolCalls]);
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
          <Sparkles size={14} className="text-purple" />
          <span className="text-t-2 text-sm font-medium">Assistant</span>
          {currentAgent && (
            <div className="flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-bg-2">
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
          <div className="flex flex-col items-center justify-center h-full text-t-3 text-xs gap-2">
            <Sparkles size={24} className="text-t-4" />
            <span>Start a conversation...</span>
          </div>
        )}
        {messages.map((msg) => (
          <MessageRenderer
            key={msg.id}
            message={msg}
            agentName={msg.agentName ?? currentAgent}
            toolCalls={toolCallsByMessage.get(msg.id) ?? []}
          />
        ))}
        {isStreaming && messages[messages.length - 1]?.role !== "assistant" && (
          <div className="flex gap-2 items-center text-t-3 text-xs">
            <StreamingDots />
            <span>Thinking...</span>
          </div>
        )}
      </div>

      {/* Input */}
      <div className="border-t border-border px-4 py-3">
        <div className="flex items-end gap-2 rounded-lg border border-border bg-bg-2 px-3 py-2 max-w-3xl mx-auto">
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
          <button
            onClick={handleSend}
            disabled={!input.trim() || isStreaming}
            className="flex items-center justify-center rounded-md p-1.5 transition-colors disabled:opacity-30 flex-shrink-0 mb-0.5"
            style={{
              background:
                input.trim() && !isStreaming
                  ? "linear-gradient(135deg, var(--blue), var(--purple))"
                  : undefined,
            }}
          >
            <Send size={14} className="text-white" />
          </button>
        </div>
      </div>
    </section>
  );
}
