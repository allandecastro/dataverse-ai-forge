"use client";

import { PlugZap } from "lucide-react";

interface ProjectConnectionBannerProps {
  onConfigure: () => void;
}

/**
 * Shown while the open project has no Dataverse connection. Connections are per
 * project, so a new project starts without one and the agents can't reach Dataverse
 * until it is set.
 */
export function ProjectConnectionBanner({ onConfigure }: ProjectConnectionBannerProps) {
  return (
    <div className="flex items-center justify-center gap-3 bg-orange-bg text-t-1 px-4 py-1.5 text-xs" role="status">
      <PlugZap size={12} className="text-orange" />
      <span>This project has no Dataverse connection yet.</span>
      <button
        onClick={onConfigure}
        className="rounded px-2 py-0.5 border border-orange/40 hover:bg-orange/10 transition-colors"
      >
        Configure connection
      </button>
    </div>
  );
}
