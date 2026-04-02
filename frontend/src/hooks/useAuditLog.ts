"use client";

import { useState, useEffect, useCallback, useRef } from "react";
import type { DiagnosticEntry } from "@/lib/types";
import { BACKEND_URL } from "@/lib/constants";

interface AuditEntry {
  entryId: string;
  timestamp: string;
  agentName: string;
  action: string;
  detail: string;
  success: boolean;
}

const POLL_INTERVAL = 30_000;

function mapAuditToEntry(audit: AuditEntry): DiagnosticEntry {
  return {
    id: `audit-${audit.entryId}`,
    agent: audit.agentName,
    toolName: audit.action,
    timestamp: new Date(audit.timestamp).getTime(),
    duration: null,
    payload: null,
    result: audit.detail,
    status: audit.success ? "complete" : "error",
  };
}

export function useAuditLog(sessionId: string | null) {
  const [auditEntries, setAuditEntries] = useState<DiagnosticEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const refetch = useCallback(async () => {
    if (!sessionId) {
      setAuditEntries([]);
      return;
    }
    setLoading(true);
    try {
      const res = await fetch(`${BACKEND_URL}/api/projects/${sessionId}/audit`);
      if (res.ok) {
        const data = await res.json();
        const entries: AuditEntry[] = data.entries ?? [];
        setAuditEntries(entries.map(mapAuditToEntry));
      }
    } catch {
      // Backend may not be running — graceful degradation
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  useEffect(() => {
    refetch();
    if (intervalRef.current) clearInterval(intervalRef.current);
    intervalRef.current = setInterval(refetch, POLL_INTERVAL);
    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, [refetch]);

  return { auditEntries, loading, refetch };
}
