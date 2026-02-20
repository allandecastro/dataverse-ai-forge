"use client";

import { agents, defaultAgent } from "@/lib/agents";
import { AgentDot } from "@/components/primitives/AgentDot";

interface AgentBubbleProps {
  agentName: string;
  content: string;
}

export function AgentBubble({ agentName, content }: AgentBubbleProps) {
  const agent = agents[agentName] ?? defaultAgent;

  return (
    <div className="flex gap-2 max-w-[90%]">
      <AgentDot agentName={agentName} />
      <div className="flex flex-col gap-1">
        <span className="text-t-3" style={{ fontSize: 11 }}>
          {agent.displayName}
        </span>
        <div
          className="bg-bg-2 text-t-2 px-3 py-2 text-sm leading-relaxed whitespace-pre-wrap"
          style={{ borderRadius: "3px 12px 12px 12px" }}
        >
          {content}
        </div>
      </div>
    </div>
  );
}
