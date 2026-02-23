"use client";

import { useState, useEffect, useCallback } from "react";

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
  conversationSummary: string;
}

const BACKEND_URL = process.env.NEXT_PUBLIC_BACKEND_URL || "http://localhost:5000";
const STORAGE_KEY = "dataverse-forge-session-id";

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
    localStorage.setItem(STORAGE_KEY, id);
  }, []);

  const createNewSession = useCallback(() => {
    const id = crypto.randomUUID().replace(/-/g, "");
    selectSession(id);
    return id;
  }, [selectSession]);

  const deleteSession = useCallback(
    async (id: string) => {
      try {
        await fetch(`${BACKEND_URL}/api/sessions/${id}`, { method: "DELETE" });
      } catch {
        // Ignore errors
      }
      if (selectedSessionId === id) {
        setSelectedSessionId(null);
        localStorage.removeItem(STORAGE_KEY);
      }
      await fetchSessions();
    },
    [selectedSessionId, fetchSessions],
  );

  // On mount: restore session from localStorage and fetch sessions
  useEffect(() => {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) {
      setSelectedSessionId(stored);
    }
    fetchSessions();
  }, [fetchSessions]);

  return {
    sessions,
    loading,
    selectedSessionId,
    fetchSessions,
    selectSession,
    createNewSession,
    deleteSession,
  };
}
