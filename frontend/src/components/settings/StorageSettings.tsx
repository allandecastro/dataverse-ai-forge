"use client";

import { useEffect, useState } from "react";
import { HardDrive, Loader2, ShieldCheck } from "lucide-react";
import { BACKEND_URL } from "@/lib/constants";

interface StorageFile {
  name: string;
  description: string;
  sizeBytes: number;
}

interface StorageInfo {
  dataDir: string;
  envVar: string;
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
        <p className="text-t-4 text-xs mt-1">
          Override with the <code className="font-mono text-t-3">{info.envVar}</code> environment variable.
        </p>
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
