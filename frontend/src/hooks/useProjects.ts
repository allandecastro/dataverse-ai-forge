"use client";

import { useState, useEffect, useCallback } from "react";
import { BACKEND_URL, STORAGE_SESSION_ID } from "@/lib/constants";

export interface ProjectSession {
  sessionId: string;
  projectName: string;
  environmentUrl: string;
  solutionName: string;
  activeAgent: string;
  tableCount: number;
  fieldCount: number;
  createdAt: string;
  lastActivityAt: string;
}

export function useProjects() {
  const [sessions, setSessions] = useState<ProjectSession[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);

  const fetchSessions = useCallback(async () => {
    setLoading(true);
    try {
      const res = await fetch(`${BACKEND_URL}/api/sessions`);
      if (res.ok) {
        const data = await res.json();
        setSessions(data.sessions ?? []);
      }
    } catch {
      // Backend may not be running — silently ignore
    } finally {
      setLoading(false);
    }
  }, []);

  const selectSession = useCallback((id: string) => {
    setSelectedSessionId(id);
    localStorage.setItem(STORAGE_SESSION_ID, id);
  }, []);

  const createNewSession = useCallback(
    async (projectName: string): Promise<string | null> => {
      const trimmed = projectName.trim();
      if (!trimmed) return null;
      try {
        const res = await fetch(`${BACKEND_URL}/api/sessions`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ projectName: trimmed }),
        });
        if (!res.ok) return null;
        const data = await res.json();
        const id: string = data.sessionId;
        selectSession(id);
        await fetchSessions();
        return id;
      } catch {
        return null;
      }
    },
    [selectSession, fetchSessions],
  );

  const renameSession = useCallback(
    async (id: string, projectName: string): Promise<boolean> => {
      const trimmed = projectName.trim();
      if (!trimmed) return false;
      try {
        const res = await fetch(`${BACKEND_URL}/api/sessions/${id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ projectName: trimmed }),
        });
        if (!res.ok) return false;
        await fetchSessions();
        return true;
      } catch {
        return false;
      }
    },
    [fetchSessions],
  );

  const deleteSession = useCallback(
    async (id: string) => {
      try {
        await fetch(`${BACKEND_URL}/api/sessions/${id}`, { method: "DELETE" });
      } catch {
        // Ignore errors
      }
      if (selectedSessionId === id) {
        setSelectedSessionId(null);
        localStorage.removeItem(STORAGE_SESSION_ID);
      }
      await fetchSessions();
    },
    [selectedSessionId, fetchSessions],
  );

  // On mount: restore session from localStorage and fetch sessions.
  // queueMicrotask defers the setSelectedSessionId so React 19's
  // set-state-in-effect lint passes. fetchSessions is async/setState too,
  // so it shares the same microtask.
  useEffect(() => {
    const stored = localStorage.getItem(STORAGE_SESSION_ID);
    queueMicrotask(() => {
      if (stored) {
        setSelectedSessionId(stored);
      }
      void fetchSessions();
    });
  }, [fetchSessions]);

  return {
    sessions,
    loading,
    selectedSessionId,
    fetchSessions,
    selectSession,
    createNewSession,
    renameSession,
    deleteSession,
  };
}
