"use client";

import { agents, defaultAgent } from "@/lib/agents";

interface AgentDotProps {
  agentName: string;
  size?: number;
}

export function AgentDot({ agentName, size = 22 }: AgentDotProps) {
  const agent = agents[agentName] ?? defaultAgent;
  const Icon = agent.icon;
  const iconSize = Math.round(size * 0.55);

  return (
    <div
      className="flex items-center justify-center rounded-md bg-bg-3 flex-shrink-0"
      style={{ width: size, height: size, borderRadius: 6 }}
    >
      <Icon size={iconSize} style={{ color: agent.color }} />
    </div>
  );
}
