"use client";

import { useRef, useEffect, useState, useCallback, useMemo } from "react";
import { Sparkles, Bug, Send } from "lucide-react";
import { IconBtn } from "@/components/primitives/IconBtn";
import { MessageRenderer } from "./MessageRenderer";
import { useForgeChatContext } from "@/hooks/useForgeChat";

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
  const { messages, sendMessage, isStreaming, toolCalls } = useForgeChatContext();

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

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages]);

  const handleSend = useCallback(async () => {
    const text = input.trim();
    if (!text || isStreaming) return;
    setInput("");
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
        </div>
        <div className="flex items-center gap-1">
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
            <div className="flex gap-0.5 items-center">
              {[0, 1, 2].map((i) => (
                <span
                  key={i}
                  className="block w-1.5 h-1.5 rounded-full bg-purple"
                  style={{
                    animation: "streaming-dots 1.4s infinite ease-in-out both",
                    animationDelay: `${i * 0.16}s`,
                  }}
                />
              ))}
            </div>
            <span>Thinking...</span>
          </div>
        )}
      </div>

      {/* Input */}
      <div className="border-t border-border px-4 py-3">
        <div className="flex items-center gap-2 rounded-lg border border-border bg-bg-2 px-3 py-2 max-w-3xl mx-auto">
          <input
            type="text"
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Ask about your Dataverse solution..."
            className="flex-1 bg-transparent text-sm text-t-1 placeholder:text-t-4 outline-none"
          />
          <button
            onClick={handleSend}
            disabled={!input.trim() || isStreaming}
            className="flex items-center justify-center rounded-md p-1.5 transition-colors disabled:opacity-30"
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
