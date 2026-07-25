"use client";

import { Sparkles, ArrowRight, Settings } from "lucide-react";

/**
 * First-run nudge — empty chat shows example prompts the user can click to
 * start. Each prompt is realistic of what the SolutionArchitect actually
 * handles, so a click triggers a sensible flow rather than a "huh?" reply.
 */

export const EXAMPLE_PROMPTS = [
  "Show me my Dataverse environment",
  "Add a Phone field to the Account table",
  "Create a CustomerFeedback table with 5 fields",
  "Add the existing Contact table to my solution",
] as const;

interface EmptyChatProps {
  onPromptClick: (prompt: string) => void;
  disabled?: boolean;
  /** True once the health probe has resolved — gates the setup nudge so it
   * doesn't flash during the initial load before status is known. */
  healthKnown?: boolean;
  /** Azure AI reachable — the hard requirement to chat at all (it's the LLM). */
  azureReady?: boolean;
  /** Dataverse connected — required to actually build/inspect solutions. */
  dataverseReady?: boolean;
  /** Opens the Settings modal so the user can fix a missing connection. */
  onOpenSettings?: () => void;
}

export function EmptyChat({
  onPromptClick,
  disabled,
  healthKnown,
  azureReady,
  dataverseReady,
  onOpenSettings,
}: EmptyChatProps) {
  // Turn the inevitable "clicked a prompt, got a connection error" first-run
  // failure into guidance: if a required connection is missing, point at
  // Settings before the user hits the wall.
  const needsSetup = healthKnown && (!azureReady || !dataverseReady);
  const setupMessage =
    !azureReady && !dataverseReady
      ? "Connect Azure AI and Dataverse in Settings to get started"
      : !azureReady
        ? "Connect Azure AI in Settings to start chatting"
        : "Connect Dataverse in Settings to build solutions";

  return (
    <div className="flex flex-col items-center justify-center h-full px-4 gap-4">
      <div className="flex flex-col items-center gap-2 text-t-3">
        <Sparkles size={24} className="text-t-4" />
        <span className="text-xs">Start a conversation, or try one of these:</span>
      </div>
      {needsSetup && onOpenSettings && (
        <button
          onClick={onOpenSettings}
          className="flex items-center gap-2 border border-border-hi bg-bg-2 px-3 py-2 text-xs text-t-2 hover:border-accent-ink/40 hover:bg-bg-3 transition-colors w-full max-w-md"
        >
          <Settings size={14} className="text-accent-ink flex-shrink-0" />
          <span className="flex-1 text-left">{setupMessage}</span>
          <ArrowRight size={12} className="text-t-4 flex-shrink-0" />
        </button>
      )}
      <div className="flex flex-col gap-2 w-full max-w-md">
        {EXAMPLE_PROMPTS.map((prompt) => (
          <button
            key={prompt}
            onClick={() => onPromptClick(prompt)}
            disabled={disabled}
            className="group flex items-center justify-between gap-2 rounded-lg border border-border bg-bg-2 px-3 py-2.5 text-left text-xs text-t-2 hover:border-accent-ink/40 hover:bg-bg-3 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          >
            <span className="flex-1">{prompt}</span>
            <ArrowRight
              size={12}
              className="text-t-4 group-hover:text-accent-ink transition-colors flex-shrink-0"
            />
          </button>
        ))}
      </div>
    </div>
  );
}
