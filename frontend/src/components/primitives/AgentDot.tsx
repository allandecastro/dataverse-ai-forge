"use client";

import { agents, defaultAgent } from "@/lib/agents";

interface AgentDotProps {
  agentName: string;
  size?: number;
}

export function AgentDot({ agentName, size = 22 }: AgentDotProps) {
  const agent = agents[agentName] ?? defaultAgent;
  const Icon = agent.icon;
  // Flat identity — a bare glyph in the agent's colour, no background chip.
  const iconSize = Math.round(size * 0.82);

  return (
    <div
      className="flex items-center justify-center flex-shrink-0"
      style={{ width: size, height: size }}
    >
      <Icon size={iconSize} style={{ color: agent.color }} />
    </div>
  );
}
