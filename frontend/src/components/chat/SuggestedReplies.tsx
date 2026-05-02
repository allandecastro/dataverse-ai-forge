"use client";

import { Sparkles } from "lucide-react";

interface SuggestedRepliesProps {
  /** Suggestions surfaced by the agent's `suggest_replies` tool. Empty/undefined = render nothing. */
  suggestions: readonly string[] | undefined;
  /** True while a stream is in flight — chips are disabled in that case. */
  disabled: boolean;
  /** Click handler — the parent decides whether to send to chat, prefill the input, etc. */
  onPick: (text: string) => void;
}

/**
 * Horizontal row of clickable reply chips rendered under the most recent
 * agent bubble. Mirrors the empty-state example prompts pattern but is
 * driven by the agent's `suggest_replies` tool, so the suggestions follow
 * the conversation context rather than being static.
 */
export function SuggestedReplies({ suggestions, disabled, onPick }: SuggestedRepliesProps) {
  if (!suggestions || suggestions.length === 0) return null;

  return (
    <div
      className="flex flex-wrap gap-1.5 mt-2 ml-7"
      style={{ animation: "phase-fade-in 0.2s ease-out" }}
    >
      <Sparkles size={12} className="text-purple mt-1.5 flex-shrink-0" />
      {suggestions.map((text, i) => (
        <button
          key={`${i}-${text}`}
          onClick={() => onPick(text)}
          disabled={disabled}
          className="text-xs px-2.5 py-1 rounded-full bg-bg-2 hover:bg-bg-3 border border-border text-t-2 hover:text-t-1 transition-colors disabled:opacity-40 disabled:cursor-not-allowed text-left max-w-[280px] truncate"
          title={text}
        >
          {text}
        </button>
      ))}
    </div>
  );
}
