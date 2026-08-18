"use client";

import { Sparkles, ArrowRight, Settings, CheckCircle2, Circle } from "lucide-react";

/**
 * Empty-chat state. Two modes:
 *  - First run / not connected → a welcome + setup checklist that points at
 *    Settings, instead of example prompts that would just fail on a missing
 *    connection.
 *  - Ready → clickable example prompts, each realistic of what the
 *    SolutionArchitect handles so a click starts a sensible flow.
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
  /** True once the health probe has resolved — gates the setup panel so it
   * doesn't flash during the initial load before status is known. */
  healthKnown?: boolean;
  /** Azure AI reachable — the hard requirement to chat at all (it's the LLM). */
  azureReady?: boolean;
  /** Dataverse connected — required to actually build/inspect solutions. */
  dataverseReady?: boolean;
  /** Opens the Settings modal so the user can fix a missing connection. */
  onOpenSettings?: () => void;
}

function ChecklistRow({ ready, label, hint }: { ready: boolean; label: string; hint: string }) {
  return (
    <div className="flex items-start gap-2.5">
      {ready ? (
        <CheckCircle2 size={16} className="text-green flex-shrink-0 mt-0.5" />
      ) : (
        <Circle size={16} className="text-t-4 flex-shrink-0 mt-0.5" />
      )}
      <div className="min-w-0">
        <div className={`text-xs font-medium ${ready ? "text-t-2" : "text-t-1"}`}>
          {label}
          {ready && <span className="text-green ml-1.5 font-normal">Connected</span>}
        </div>
        <div className="text-t-4 text-xs">{hint}</div>
      </div>
    </div>
  );
}

export function EmptyChat({
  onPromptClick,
  disabled,
  healthKnown,
  azureReady,
  dataverseReady,
  onOpenSettings,
}: EmptyChatProps) {
  const needsSetup = healthKnown && (!azureReady || !dataverseReady);

  // First-run / not-connected: a welcome + what-to-connect panel. Example
  // prompts are hidden here because they'd fail on the missing connection.
  if (needsSetup) {
    return (
      <div className="flex flex-col items-center justify-center h-full px-4">
        <div className="w-full max-w-md flex flex-col gap-5">
          <div className="flex flex-col items-center gap-2 text-center">
            <div className="flex items-center justify-center w-12 h-12 rounded-xl bg-bg-2 border border-border">
              <Sparkles size={22} className="text-accent-ink" />
            </div>
            <h2 className="text-t-1 text-sm font-semibold">Welcome to Dataverse AI Forge</h2>
            <p className="text-t-3 text-xs leading-relaxed">
              Build Dataverse solutions by chatting. Connect your two services to get started.
            </p>
          </div>

          <div className="flex flex-col gap-3 border border-border bg-bg-1 rounded-lg px-4 py-3.5">
            <ChecklistRow
              ready={!!azureReady}
              label="Azure AI"
              hint="The model that powers the agents. Required to chat."
            />
            <ChecklistRow
              ready={!!dataverseReady}
              label="Dataverse"
              hint="Your environment. Required to inspect and build solutions."
            />
          </div>

          {onOpenSettings && (
            <button
              onClick={onOpenSettings}
              className="flex items-center justify-center gap-2 bg-accent text-accent-fg px-3 py-2.5 text-xs font-medium hover:opacity-90 transition-opacity"
            >
              <Settings size={14} />
              Open Settings to connect
              <ArrowRight size={12} />
            </button>
          )}
        </div>
      </div>
    );
  }

  // Ready: example prompts.
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
