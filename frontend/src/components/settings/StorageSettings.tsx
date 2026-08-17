"use client";

import { useEffect, useState } from "react";
import { HardDrive, Loader2, ShieldCheck, FolderOpen, RotateCcw, AlertTriangle } from "lucide-react";
import { BACKEND_URL } from "@/lib/constants";
import { getElectronAPI } from "@/lib/electron";

interface StorageFile {
  name: string;
  description: string;
  sizeBytes: number;
}

interface StorageInfo {
  dataDir: string;
  envVar: string;
  isPinnedByEnv: boolean;
  note: string;
  files: StorageFile[];
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function StorageSettings() {
  const [info, setInfo] = useState<StorageInfo | null>(null);
  const [loading, setLoading] = useState(true);

  // Change-location flow state.
  const [target, setTarget] = useState("");
  const [moving, setMoving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pendingTarget, setPendingTarget] = useState<string | null>(null);

  const electron = getElectronAPI();

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await fetch(`${BACKEND_URL}/api/settings/storage`);
        if (res.ok && !cancelled) setInfo((await res.json()) as StorageInfo);
      } catch {
        // Leave info null; the panel renders an "unavailable" note.
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  async function pickFolder() {
    setError(null);
    if (!electron) return;
    const picked = await electron.pickFolder();
    if (picked) {
      setTarget(picked);
      await submitMove(picked);
    }
  }

  async function submitMove(chosen: string) {
    const path = chosen.trim();
    if (!path) return;
    setMoving(true);
    setError(null);
    try {
      const res = await fetch(`${BACKEND_URL}/api/settings/storage/move`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ target: path }),
      });
      const data = (await res.json()) as { target?: string; message?: string };
      if (!res.ok) {
        setError(data.message ?? "Could not use that folder.");
        return;
      }
      setPendingTarget(data.target ?? path);
    } catch {
      setError("Could not reach the backend to save the new location.");
    } finally {
      setMoving(false);
    }
  }

  if (loading) {
    return (
      <div className="flex items-center gap-2 text-t-3 text-sm">
        <Loader2 size={14} className="animate-spin" /> Loading…
      </div>
    );
  }

  if (!info) {
    return <p className="text-t-3 text-sm">Storage info is unavailable right now.</p>;
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-2">
        <HardDrive size={16} className="text-accent-ink" />
        <h3 className="text-t-1 text-sm font-semibold">Data &amp; storage</h3>
      </div>

      <div className="flex items-start gap-2 text-t-2 text-sm leading-relaxed">
        <ShieldCheck size={16} className="text-green flex-shrink-0 mt-0.5" />
        <p>{info.note}</p>
      </div>

      <div>
        <div className="text-t-3 text-xs mb-1">Location on this machine</div>
        <code className="block bg-code-bg text-code-text text-xs font-mono px-2.5 py-2 break-all">
          {info.dataDir}
        </code>

        {info.isPinnedByEnv ? (
          <p className="text-t-4 text-xs mt-1.5">
            Pinned by the <code className="font-mono text-t-3">{info.envVar}</code> environment
            variable. Unset it to choose a folder from here.
          </p>
        ) : pendingTarget ? (
          // Move recorded — the backend relocates the files on the next startup.
          <div className="mt-2 flex flex-col gap-2 border border-green-bg bg-green-bg px-3 py-2.5 text-xs">
            <div className="text-t-1">
              New location saved:
              <code className="ml-1 font-mono text-green break-all">{pendingTarget}</code>
            </div>
            <div className="text-t-2">Restart the app to move your data there.</div>
            {electron ? (
              <button
                onClick={() => electron.relaunch()}
                className="self-start flex items-center gap-1.5 bg-accent text-accent-fg px-2.5 py-1 hover:opacity-90 transition-opacity"
              >
                <RotateCcw size={12} /> Restart now
              </button>
            ) : (
              <div className="text-t-3">Stop and start the backend, then reload this page.</div>
            )}
          </div>
        ) : (
          <div className="mt-2 flex flex-col gap-2">
            {electron ? (
              // Desktop: native folder picker (move submits on pick).
              <button
                onClick={pickFolder}
                disabled={moving}
                className="self-start flex items-center gap-1.5 border border-border bg-bg-2 px-2.5 py-1 text-xs text-t-2 hover:bg-bg-3 hover:text-t-1 transition-colors disabled:opacity-50"
              >
                {moving ? <Loader2 size={12} className="animate-spin" /> : <FolderOpen size={12} />}
                Change location…
              </button>
            ) : (
              // Browser/dev: no native picker, take an absolute path.
              <div className="flex flex-col gap-1.5">
                <div className="flex items-center gap-2">
                  <input
                    value={target}
                    onChange={(e) => setTarget(e.target.value)}
                    placeholder="/absolute/path/to/folder"
                    className="flex-1 min-w-0 bg-bg-2 border border-border px-2 py-1 text-xs font-mono text-t-1 placeholder:text-t-4 focus:outline-none focus:border-accent-ink"
                  />
                  <button
                    onClick={() => submitMove(target)}
                    disabled={moving || !target.trim()}
                    className="flex items-center gap-1.5 border border-border bg-bg-2 px-2.5 py-1 text-xs text-t-2 hover:bg-bg-3 hover:text-t-1 transition-colors disabled:opacity-50"
                  >
                    {moving ? <Loader2 size={12} className="animate-spin" /> : <FolderOpen size={12} />}
                    Change
                  </button>
                </div>
                <p className="text-t-4 text-xs">
                  Or set the <code className="font-mono text-t-3">{info.envVar}</code> environment variable.
                </p>
              </div>
            )}

            {error && (
              <div className="flex items-start gap-1.5 text-red text-xs">
                <AlertTriangle size={13} className="flex-shrink-0 mt-px" />
                <span>{error}</span>
              </div>
            )}
          </div>
        )}
      </div>

      <div>
        <div className="text-t-3 text-xs mb-1.5">What&apos;s stored here</div>
        <div className="flex flex-col divide-y divide-border border border-border">
          {info.files.length === 0 ? (
            <div className="px-3 py-2 text-t-3 text-xs">Nothing stored yet.</div>
          ) : (
            info.files.map((f) => (
              <div key={f.name} className="flex items-start justify-between gap-3 px-3 py-2">
                <div className="min-w-0">
                  <div className="text-t-1 text-xs font-mono">{f.name}</div>
                  <div className="text-t-3 text-xs">{f.description}</div>
                </div>
                <div className="text-t-4 text-xs font-mono flex-shrink-0">{formatSize(f.sizeBytes)}</div>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}
