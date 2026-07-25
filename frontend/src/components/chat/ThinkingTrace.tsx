"use client";

import { useState } from "react";
import { ChevronRight, ChevronDown } from "lucide-react";
import { agents, defaultAgent } from "@/lib/agents";

interface ThinkingTraceProps {
  agentName: string;
  reasoning: string;
  /** True when this belongs to the actively-streaming bubble — auto-expands and
   * shows a pulse so the user notices the reasoning as it unfolds, instead of
   * having to know to click a collapsed "Reasoning" toggle mid-stream. */
  live?: boolean;
}

export function ThinkingTrace({ agentName, reasoning, live }: ThinkingTraceProps) {
  const [expanded, setExpanded] = useState(!!live);
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
        {live && (
          <span
            aria-hidden="true"
            className="ml-0.5 inline-block rounded-full bg-accent-ink"
            style={{ width: 5, height: 5, animation: "forge-blink 1.4s ease-in-out infinite" }}
          />
        )}
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
