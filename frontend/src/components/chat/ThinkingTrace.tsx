"use client";

import { useState } from "react";
import { ChevronRight, ChevronDown } from "lucide-react";
import { agents, defaultAgent } from "@/lib/agents";

interface ThinkingTraceProps {
  agentName: string;
  reasoning: string;
}

export function ThinkingTrace({ agentName, reasoning }: ThinkingTraceProps) {
  const [expanded, setExpanded] = useState(false);
  const agent = agents[agentName] ?? defaultAgent;

  return (
    <div className="mb-1">
      <button
        onClick={() => setExpanded(!expanded)}
        className="flex items-center gap-1 text-t-3 hover:text-t-2"
        style={{ fontSize: 11 }}
      >
        {expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
        <span className="italic">Reasoning</span>
      </button>
      {expanded && (
        <div
          className="bg-bg-2 text-t-3 italic mt-1 px-3 py-2 whitespace-pre-wrap font-mono"
          style={{
            fontSize: 11,
            borderLeft: `3px dashed ${agent.color}`,
          }}
        >
          {reasoning}
        </div>
      )}
    </div>
  );
}
