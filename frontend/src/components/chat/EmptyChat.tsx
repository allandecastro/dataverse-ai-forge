"use client";

import { Sparkles, ArrowRight } from "lucide-react";

/**
 * First-run nudge — empty chat shows example prompts the user can click to
 * start. Each prompt is realistic of what the SolutionArchitect actually
 * handles, so a click triggers a sensible flow rather than a "huh?" reply.
 */

const EXAMPLE_PROMPTS = [
  "Show me my Dataverse environment",
  "Add a Phone field to the Account table",
  "Create a CustomerFeedback table with 5 fields",
  "Add the existing Contact table to my solution",
] as const;

interface EmptyChatProps {
  onPromptClick: (prompt: string) => void;
  disabled?: boolean;
}

export function EmptyChat({ onPromptClick, disabled }: EmptyChatProps) {
  return (
    <div className="flex flex-col items-center justify-center h-full px-4 gap-4">
      <div className="flex flex-col items-center gap-2 text-t-3">
        <Sparkles size={24} className="text-t-4" />
        <span className="text-xs">Start a conversation, or try one of these:</span>
      </div>
      <div className="flex flex-col gap-2 w-full max-w-md">
        {EXAMPLE_PROMPTS.map((prompt) => (
          <button
            key={prompt}
            onClick={() => onPromptClick(prompt)}
            disabled={disabled}
            className="group flex items-center justify-between gap-2 rounded-lg border border-border bg-bg-2 px-3 py-2.5 text-left text-xs text-t-2 hover:border-blue/40 hover:bg-bg-3 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          >
            <span className="flex-1">{prompt}</span>
            <ArrowRight
              size={12}
              className="text-t-4 group-hover:text-blue transition-colors flex-shrink-0"
            />
          </button>
        ))}
      </div>
    </div>
  );
}
