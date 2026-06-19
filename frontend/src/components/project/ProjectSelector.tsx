"use client";

import { useState, useCallback } from "react";
import { FolderOpen, Plus, Trash2, Clock, ArrowLeft } from "lucide-react";
import type { ProjectSession } from "@/hooks/useProjects";

interface ProjectSelectorProps {
  sessions: ProjectSession[];
  loading: boolean;
  onSelect: (id: string) => void;
  onNewProject: (name: string) => Promise<string | null> | string | null;
  onDelete: (id: string) => void;
}

function relativeTime(dateStr: string): string {
  const now = Date.now();
  const then = new Date(dateStr).getTime();
  const diffMs = now - then;
  const diffMin = Math.floor(diffMs / 60000);
  if (diffMin < 1) return "just now";
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHr = Math.floor(diffMin / 60);
  if (diffHr < 24) return `${diffHr}h ago`;
  const diffDay = Math.floor(diffHr / 24);
  return `${diffDay}d ago`;
}

export function ProjectSelector({
  sessions,
  loading,
  onSelect,
  onNewProject,
  onDelete,
}: ProjectSelectorProps) {
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = useCallback(async () => {
    const trimmed = name.trim();
    if (!trimmed) {
      setError("Project name is required");
      return;
    }
    setSubmitting(true);
    setError(null);
    const result = await onNewProject(trimmed);
    setSubmitting(false);
    if (!result) {
      setError("Could not create project — is the backend running?");
      return;
    }
    setCreating(false);
    setName("");
  }, [name, onNewProject]);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
      <div className="w-full max-w-xl rounded-xl border border-border bg-bg-1 shadow-2xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div className="flex items-center gap-2">
            {creating && (
              <button
                onClick={() => { setCreating(false); setError(null); }}
                className="p-1 rounded hover:bg-bg-2 transition-colors"
                title="Back"
              >
                <ArrowLeft size={16} className="text-t-2" />
              </button>
            )}
            <FolderOpen size={18} className="text-accent-ink" />
            <h2 className="text-lg font-semibold text-t-1">
              {creating ? "Name your project" : "Projects"}
            </h2>
          </div>
          {!creating && (
            <button
              onClick={() => setCreating(true)}
              className="flex items-center gap-1.5 rounded-lg bg-accent px-3 py-1.5 text-sm font-medium text-accent-fg hover:opacity-90 transition-opacity"
            >
              <Plus size={14} />
              New Project
            </button>
          )}
        </div>

        {/* Body */}
        {creating ? (
          <div className="p-6 space-y-3">
            <label className="block text-xs text-t-3 uppercase tracking-wide">
              Project name
            </label>
            <input
              autoFocus
              type="text"
              value={name}
              onChange={(e) => { setName(e.target.value); setError(null); }}
              onKeyDown={(e) => {
                if (e.key === "Enter") void submit();
                if (e.key === "Escape") { setCreating(false); setError(null); }
              }}
              placeholder="e.g. Feedback Training"
              className="w-full rounded-lg border border-border bg-bg-0 px-3 py-2 text-sm text-t-1 placeholder:text-t-3 focus:outline-none focus:border-accent-ink"
              disabled={submitting}
            />
            {error && <p className="text-xs text-red">{error}</p>}
            <div className="flex justify-end gap-2 pt-1">
              <button
                onClick={() => { setCreating(false); setError(null); }}
                className="rounded-lg border border-border px-3 py-1.5 text-sm text-t-2 hover:bg-bg-2 transition-colors"
                disabled={submitting}
              >
                Cancel
              </button>
              <button
                onClick={() => void submit()}
                disabled={submitting || !name.trim()}
                className="rounded-lg bg-accent px-3 py-1.5 text-sm font-medium text-accent-fg hover:opacity-90 transition-opacity disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {submitting ? "Creating..." : "Create"}
              </button>
            </div>
          </div>
        ) : (
          <div className="max-h-[400px] overflow-y-auto p-2">
            {loading ? (
              <div className="flex items-center justify-center py-12">
                <span className="text-t-3 text-sm">Loading sessions...</span>
              </div>
            ) : sessions.length === 0 ? (
              <div className="flex flex-col items-center justify-center py-12 gap-2">
                <FolderOpen size={32} className="text-t-3" />
                <span className="text-t-3 text-sm">No projects yet</span>
                <span className="text-t-3 text-xs">
                  Create a new project to get started
                </span>
              </div>
            ) : (
              sessions.map((session) => (
                <div
                  key={session.sessionId}
                  onClick={() => onSelect(session.sessionId)}
                  className="group flex items-center justify-between rounded-lg px-4 py-3 cursor-pointer hover:bg-bg-2 transition-colors"
                >
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-medium text-t-1 truncate">
                        {session.projectName || "Untitled Project"}
                      </span>
                      {session.solutionName && (
                        <span className="text-xs text-t-3 bg-bg-2 rounded px-1.5 py-0.5 flex-shrink-0">
                          {session.solutionName}
                        </span>
                      )}
                    </div>
                    <div className="flex items-center gap-3 mt-1">
                      {session.tableCount > 0 && (
                        <span className="text-xs text-t-3">
                          {session.tableCount} tables
                        </span>
                      )}
                      {session.fieldCount > 0 && (
                        <span className="text-xs text-t-3">
                          {session.fieldCount} fields
                        </span>
                      )}
                    </div>
                  </div>
                  <div className="flex items-center gap-2 flex-shrink-0 ml-3">
                    <span className="flex items-center gap-1 text-xs text-t-3">
                      <Clock size={12} />
                      {relativeTime(session.lastActivityAt)}
                    </span>
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        onDelete(session.sessionId);
                      }}
                      className="p-1 rounded opacity-0 group-hover:opacity-100 hover:bg-red/10 hover:text-red transition-all"
                      title="Delete project"
                    >
                      <Trash2 size={14} />
                    </button>
                  </div>
                </div>
              ))
            )}
          </div>
        )}
      </div>
    </div>
  );
}
