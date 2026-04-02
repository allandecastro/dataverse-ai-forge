"use client";

import { useRef, useEffect, useState, useCallback } from "react";
import { Sparkles, Bug, X, Send } from "lucide-react";
import { IconBtn } from "@/components/primitives/IconBtn";
import { MessageRenderer } from "./MessageRenderer";
import { useForgeChatContext } from "@/hooks/useForgeChat";

interface ChatPanelProps {
  onClose: () => void;
  onToggleDiag: () => void;
  diagOpen: boolean;
  currentAgent?: string;
  width: number;
  onWidthChange: (width: number) => void;
}

const MIN_WIDTH = 320;
const MAX_WIDTH = 900;

export function ChatPanel({
  onClose,
  onToggleDiag,
  diagOpen,
  currentAgent,
  width,
  onWidthChange,
}: ChatPanelProps) {
  const { messages, sendMessage, isStreaming } = useForgeChatContext();
  const [input, setInput] = useState("");
  const scrollRef = useRef<HTMLDivElement>(null);
  const draggingRef = useRef(false);

  // Auto-scroll to bottom on new messages
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

  // Drag resize handler
  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      draggingRef.current = true;
      const startX = e.clientX;
      const startWidth = width;

      const handleMouseMove = (moveEvent: MouseEvent) => {
        if (!draggingRef.current) return;
        // Dragging left = making wider (panel is on right side, resize handle is on left edge)
        const delta = startX - moveEvent.clientX;
        const newWidth = Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, startWidth + delta));
        onWidthChange(newWidth);
      };

      const handleMouseUp = () => {
        draggingRef.current = false;
        document.removeEventListener("mousemove", handleMouseMove);
        document.removeEventListener("mouseup", handleMouseUp);
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
      };

      document.addEventListener("mousemove", handleMouseMove);
      document.addEventListener("mouseup", handleMouseUp);
      document.body.style.cursor = "col-resize";
      document.body.style.userSelect = "none";
    },
    [width, onWidthChange]
  );

  return (
    <section
      className="flex-shrink-0 bg-bg-1 flex flex-col relative"
      style={{ width }}
    >
      {/* Resize handle */}
      <div
        onMouseDown={handleMouseDown}
        className="absolute left-0 top-0 bottom-0 w-1 cursor-col-resize hover:bg-blue/40 active:bg-blue/60 transition-colors z-10"
        style={{ marginLeft: -2 }}
      />

      {/* Header */}
      <div className="flex h-12 items-center justify-between border-b border-border px-3">
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
          <IconBtn icon={X} onClick={onClose} title="Close chat" />
        </div>
      </div>

      {/* Messages */}
      <div ref={scrollRef} className="flex-1 overflow-y-auto p-3 space-y-4">
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
      <div className="border-t border-border p-3">
        <div className="flex items-center gap-2 rounded-lg border border-border bg-bg-2 px-3 py-2">
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
