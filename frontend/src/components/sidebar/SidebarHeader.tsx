"use client";

import { Sparkles, FolderOpen } from "lucide-react";

interface SidebarHeaderProps {
  collapsed: boolean;
  projectName?: string;
  onSwitchProject?: () => void;
}

export function SidebarHeader({ collapsed, projectName, onSwitchProject }: SidebarHeaderProps) {
  return (
    <div className="flex h-12 items-center gap-2.5 border-b border-border px-3">
      <div
        className="flex items-center justify-center rounded-md flex-shrink-0"
        style={{
          width: 28,
          height: 28,
          background: "linear-gradient(135deg, var(--blue), var(--purple))",
        }}
      >
        <Sparkles size={14} className="text-white" />
      </div>
      {!collapsed && (
        <div className="flex flex-col min-w-0 flex-1">
          <span className="text-t-1 font-semibold truncate" style={{ fontSize: 13 }}>
            Dataverse AI Forge
          </span>
          <div className="flex items-center gap-1">
            <span className="text-t-3 truncate" style={{ fontSize: 11 }}>
              {projectName || "New Project"}
            </span>
            {onSwitchProject && (
              <button
                onClick={onSwitchProject}
                className="flex-shrink-0 p-0.5 rounded hover:bg-bg-2 transition-colors"
                title="Switch project"
              >
                <FolderOpen size={11} className="text-t-3" />
              </button>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
