"use client";

import type { ForgeMessage, ToolCallInfo } from "@/lib/sse-types";
import { AgentBubble } from "./AgentBubble";
import { ThinkingTrace } from "./ThinkingTrace";
import { ToolCallsInline } from "./ToolCallsInline";
import { SrsCard, isSrsContent } from "./SrsCard";
import { AgentDot } from "@/components/primitives/AgentDot";
import { agents, defaultAgent } from "@/lib/agents";
import { DEFAULT_AGENT_NAME } from "@/lib/constants";

interface MessageRendererProps {
  message: ForgeMessage;
  agentName?: string;
  toolCalls?: ToolCallInfo[];
}

function parseThinking(content: string): {
  reasoning: string | null;
  cleanContent: string;
} {
  const match = content.match(/<think>([\s\S]*?)<\/think>/);
  if (!match) return { reasoning: null, cleanContent: content };
  return {
    reasoning: match[1].trim(),
    cleanContent: content.replace(match[0], "").trim(),
  };
}

export function MessageRenderer({ message, agentName: agentNameProp, toolCalls = [] }: MessageRendererProps) {
  const content = message.content;
  if (!content) return null;

  if (message.role === "user") {
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
