"use client";

import { memo } from "react";
import type { ForgeMessage, ToolCallInfo } from "@/lib/sse-types";
import { AgentBubble } from "./AgentBubble";
import { ThinkingTrace } from "./ThinkingTrace";
import { ToolCallsInline } from "./ToolCallsInline";
import { SrsCard, isSrsContent } from "./SrsCard";
import { AgentDot } from "@/components/primitives/AgentDot";
import { agents, defaultAgent } from "@/lib/agents";
import { DEFAULT_AGENT_NAME } from "@/lib/constants";
import { AlertCircle, RotateCcw, Check, X, Pencil } from "lucide-react";

interface MessageRendererProps {
  message: ForgeMessage;
  agentName?: string;
  toolCalls?: ToolCallInfo[];
  /** Called when the user clicks Retry on a transport error bubble. */
  onRetry?: () => void;
  /** When true, the Retry button is disabled (e.g. a stream is already running). */
  retryDisabled?: boolean;
}

type ChipDescriptor = {
  Icon: typeof Check;
  iconClass: string;
  label: string;
};

function approvalChipFor(kind: NonNullable<ForgeMessage["kind"]>): ChipDescriptor | null {
  switch (kind) {
    case "approval-receipt":
      return { Icon: Check, iconClass: "text-green", label: "Approved" };
    case "approval-rejection":
      return { Icon: X, iconClass: "text-red", label: "Rejected" };
    case "approval-edit":
      return { Icon: Pencil, iconClass: "text-blue", label: "Edit requested" };
    default:
      return null;
  }
}

function parseThinking(content: string): {
  reasoning: string | null;
  cleanContent: string;
} {
  const match = content.match(/<think>([\s\S]*?)<\/think>/);
  const inner = match?.[1]?.trim();
  if (!match || !inner) return { reasoning: null, cleanContent: content };
  return {
    reasoning: inner,
    cleanContent: content.replace(match[0], "").trim(),
  };
}

function MessageRendererImpl({ message, agentName: agentNameProp, toolCalls = [], onRetry, retryDisabled }: MessageRendererProps) {
  const content = message.content;

  // Placeholder bubble — agent fired tools BEFORE its TEXT_MESSAGE_START
  // arrived (chained handoffs, new same-agent turn after user input). Render
  // just the agent header + tools so the user sees activity attributed to the
  // right agent immediately, with no jumping when the real bubble opens.
  if (message.role === "assistant" && !content && toolCalls.length > 0) {
    const placeholderAgent = message.agentName ?? agentNameProp ?? DEFAULT_AGENT_NAME;
    const placeholderAgentMeta = agents[placeholderAgent] ?? defaultAgent;
    return (
      <div>
        <div className="flex gap-2 max-w-[90%] items-center">
          <AgentDot agentName={placeholderAgent} />
          <span className="text-t-3" style={{ fontSize: 11 }}>
            {placeholderAgentMeta.displayName}
          </span>
        </div>
        <ToolCallsInline toolCalls={toolCalls} />
      </div>
    );
  }

  if (!content) return null;

  // Transport / connection error — render a compact card with a Retry button.
  if (message.role === "assistant" && message.isError) {
    return (
      <div
        className="rounded-lg bg-bg-2 px-3 py-2.5"
        style={{ borderLeft: "3px solid var(--red)", maxWidth: "90%" }}
      >
        <div className="flex items-start gap-2">
          <AlertCircle size={14} className="text-red flex-shrink-0 mt-0.5" />
          <div className="flex-1 min-w-0">
            <div className="text-t-1 text-sm leading-relaxed whitespace-pre-wrap">{content}</div>
            {onRetry && (
              <button
                onClick={onRetry}
                disabled={retryDisabled}
                className="mt-2 flex items-center gap-1 rounded px-2 py-1 text-xs font-medium text-blue hover:bg-bg-3 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <RotateCcw size={12} />
                Retry
              </button>
            )}
          </div>
        </div>
      </div>
    );
  }

  if (message.role === "user") {
    // Approval-flow signals (Approve / Reject / Edit) render as a compact,
    // centered chip rather than a chat bubble. Keeps the chat readable —
    // the user already saw the ApprovalCard, they don't need a verbose
    // "APPROVED BATCH:..." echo afterward.
    if (message.kind) {
      const chip = approvalChipFor(message.kind);
      if (chip) {
        return (
          <div className="flex justify-center my-1">
            <div
              className="flex items-center gap-1.5 rounded-full bg-bg-2 border border-border px-2.5 py-0.5 text-t-3"
              style={{ fontSize: 11 }}
            >
              <chip.Icon size={11} className={chip.iconClass} />
              <span>{chip.label}</span>
            </div>
          </div>
        );
      }
    }

    return (
      <div className="flex flex-col items-end gap-0.5">
        {message.timestamp && (
          <span className="text-t-4 mr-1" style={{ fontSize: 10, fontFamily: "var(--font-geist-mono), monospace" }}>
            {new Date(message.timestamp).toLocaleTimeString("en-US", { hour12: false, hour: "2-digit", minute: "2-digit" })}
          </span>
        )}
        <div
          className="bg-blue-bg text-t-1 px-3 py-2 text-sm leading-relaxed max-w-[80%] whitespace-pre-wrap"
          style={{ borderRadius: "12px 12px 3px 12px" }}
        >
          {content}
        </div>
      </div>
    );
  }

  const { reasoning, cleanContent } = parseThinking(content);
  const agentName = agentNameProp ?? DEFAULT_AGENT_NAME;
  const agent = agents[agentName] ?? defaultAgent;

  // Render SRS card for structured BA specs
  if (agentName === "BusinessAnalyst" && isSrsContent(cleanContent)) {
    return (
      <div>
        {reasoning && (
          <ThinkingTrace agentName={agentName} reasoning={reasoning} />
        )}
        <div className="flex gap-2 max-w-[90%]">
          <AgentDot agentName={agentName} />
          <div className="flex flex-col gap-1 flex-1">
            <span className="text-t-3" style={{ fontSize: 11 }}>
              {agent.displayName}
            </span>
            <SrsCard content={cleanContent} agentColor={agent.color} />
          </div>
        </div>
        <ToolCallsInline toolCalls={toolCalls} />
      </div>
    );
  }

  return (
    <div>
      {reasoning && (
        <ThinkingTrace agentName={agentName} reasoning={reasoning} />
      )}
      <AgentBubble agentName={agentName} content={cleanContent} timestamp={message.timestamp} />
      <ToolCallsInline toolCalls={toolCalls} />
    </div>
  );
}

/**
 * Skip re-render unless any prop's relevant signature changed. Past messages and
 * their tool calls are immutable, so once a turn finishes we stop re-rendering them
 * even when the parent re-renders for an unrelated reason (new message, streaming
 * delta on the latest bubble, etc.).
 *
 * Tool call comparison uses a content-aware check rather than reference equality
 * because attributeToolCalls rebuilds the per-message arrays each render.
 */
function arePropsEqual(prev: MessageRendererProps, next: MessageRendererProps) {
  if (prev.message !== next.message) return false;
  if (prev.agentName !== next.agentName) return false;
  if (prev.onRetry !== next.onRetry) return false;
  if (prev.retryDisabled !== next.retryDisabled) return false;

  const prevTools = prev.toolCalls ?? [];
  const nextTools = next.toolCalls ?? [];
  if (prevTools.length !== nextTools.length) return false;
  for (let i = 0; i < prevTools.length; i++) {
    const a = prevTools[i];
    const b = nextTools[i];
    if (a === b) continue;
    if (a.id !== b.id) return false;
    if (a.status !== b.status) return false;
    if (a.args !== b.args) return false;
    if (a.result !== b.result) return false;
    if (a.endTime !== b.endTime) return false;
  }
  return true;
}

export const MessageRenderer = memo(MessageRendererImpl, arePropsEqual);
