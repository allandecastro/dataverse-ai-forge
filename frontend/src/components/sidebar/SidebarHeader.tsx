"use client";

import { useState, useRef } from "react";
import { FolderOpen, Pencil, Check, X } from "lucide-react";
import { Logo } from "@/components/primitives/Logo";

interface SidebarHeaderProps {
  collapsed: boolean;
  projectName?: string;
  onSwitchProject?: () => void;
  onRenameProject?: (newName: string) => Promise<boolean> | boolean;
}

export function SidebarHeader({
  collapsed,
  projectName,
  onSwitchProject,
  onRenameProject,
}: SidebarHeaderProps) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(projectName ?? "");
  const [saving, setSaving] = useState(false);
  const inputRef = useRef<HTMLInputElement | null>(null);

  const startEditing = () => {
    setDraft(projectName ?? "");
    setEditing(true);
    requestAnimationFrame(() => inputRef.current?.select());
  };

  const cancel = () => {
    setEditing(false);
    setDraft(projectName ?? "");
  };

  const commit = async () => {
    const trimmed = draft.trim();
    if (!trimmed || trimmed === projectName) {
      setEditing(false);
      return;
    }
    if (!onRenameProject) {
      setEditing(false);
      return;
    }
    setSaving(true);
    const ok = await onRenameProject(trimmed);
    setSaving(false);
    if (ok) setEditing(false);
  };

  return (
    <div className="flex h-12 items-center gap-2.5 border-b border-border px-3">
      <Logo size={28} className="flex-shrink-0" />
      {!collapsed && (
        <div className="flex flex-col min-w-0 flex-1">
          <span className="text-t-1 font-semibold truncate" style={{ fontSize: 13 }}>
            Dataverse AI Forge
          </span>
          <div className="flex items-center gap-1 min-w-0">
            {editing ? (
              <>
                <input
                  ref={inputRef}
                  value={draft}
                  onChange={(e) => setDraft(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") void commit();
                    if (e.key === "Escape") cancel();
                  }}
                  disabled={saving}
                  className="min-w-0 flex-1 bg-bg-2 border border-border px-1 py-0.5 text-t-1 outline-none focus:ring-1 focus:ring-accent-ink"
                  style={{ fontSize: 11 }}
                />
                <button
                  onClick={() => void commit()}
                  disabled={saving}
                  className="flex-shrink-0 p-0.5 rounded hover:bg-bg-2 text-t-2 hover:text-green transition-colors disabled:opacity-50"
                  title="Save"
                >
                  <Check size={11} />
                </button>
                <button
                  onClick={cancel}
                  disabled={saving}
                  className="flex-shrink-0 p-0.5 rounded hover:bg-bg-2 text-t-2 hover:text-red transition-colors disabled:opacity-50"
                  title="Cancel"
                >
                  <X size={11} />
                </button>
              </>
            ) : (
              <>
                <span className="text-t-3 truncate" style={{ fontSize: 11 }}>
                  {projectName || "New Project"}
                </span>
                {onRenameProject && (
                  <button
                    onClick={startEditing}
                    className="flex-shrink-0 p-0.5 rounded hover:bg-bg-2 transition-colors"
                    title="Rename project"
                  >
                    <Pencil size={11} className="text-t-3" />
                  </button>
                )}
                {onSwitchProject && (
                  <button
                    onClick={onSwitchProject}
                    className="flex-shrink-0 p-0.5 rounded hover:bg-bg-2 transition-colors"
                    title="Switch project"
                  >
                    <FolderOpen size={11} className="text-t-3" />
                  </button>
                )}
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
