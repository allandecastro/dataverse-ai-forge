"use client";

import { useState, useCallback } from "react";
import { BACKEND_URL } from "@/lib/constants";

/** How the backend authenticates against Dataverse. */
export type DataverseAuthMode = "ServicePrincipal" | "Interactive";

export interface AppSettings {
  azureAiEndpoint: string;
  azureAiApiKey: string;
  azureAiDefaultModel: string;
  /**
   * Optional per-agent model overrides (agent name → model deployment name).
   * An agent absent from the map uses azureAiDefaultModel. Same endpoint + key
   * as the default; only the model name in the request body changes.
   */
  azureAiAgentModels: Record<string, string>;
  /**
   * Embeddings deployment used by the project knowledge base (Documentary Agent),
   * on the same Foundry endpoint + key. Backend falls back to text-embedding-3-small.
   */
  ragEmbeddingDeployment: string;
  // Dataverse / Azure DevOps credentials are per project — see useProjectConnection.
}

export interface HealthStatus {
  status: string;
  /** The CURRENT project's Dataverse connection (per-project; absent with no project). */
  dataverse?: { status: string; configured?: boolean; environment?: string; environmentUrl?: string; error?: string };
  azureAi?: { status: string; endpoint?: string };
}

export interface TestResult {
  success: boolean;
  message: string;
}

const emptySettings: AppSettings = {
  azureAiEndpoint: "",
  azureAiApiKey: "",
  azureAiDefaultModel: "",
  azureAiAgentModels: {},
  ragEmbeddingDeployment: "",
};

export function useSettings() {
  const [settings, setSettings] = useState<AppSettings>(emptySettings);
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);

  const fetchSettings = useCallback(async (): Promise<AppSettings | null> => {
    setLoading(true);
    try {
      const res = await fetch(`${BACKEND_URL}/api/settings`);
      if (res.ok) {
        const data: AppSettings = await res.json();
        setSettings(data);
        return data;
      }
      return null;
    } catch {
      // Backend may not be running
      return null;
    } finally {
      setLoading(false);
    }
  }, []);

  /**
   * Global health (backend + Azure AI) plus, when a project is open, that project's
   * Dataverse connection status — Dataverse is per project, so /healthz no longer
   * reports it.
   */
  const fetchHealth = useCallback(async (sessionId?: string) => {
    try {
      const res = await fetch(`${BACKEND_URL}/healthz`);
      if (!res.ok) return;
      const data: HealthStatus = await res.json();
      if (sessionId) {
        try {
          const dv = await fetch(`${BACKEND_URL}/api/sessions/${encodeURIComponent(sessionId)}/connection/status`);
          if (dv.ok) data.dataverse = await dv.json();
        } catch {
          // Keep the global part; the sidebar just shows Dataverse as unknown.
        }
      }
      setHealth(data);
    } catch {
      setHealth({ status: "unavailable" });
    }
  }, []);

  const saveSettings = useCallback(async (dto: AppSettings): Promise<boolean> => {
    setSaving(true);
    try {
      const res = await fetch(`${BACKEND_URL}/api/settings`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(dto),
      });
      if (res.ok) {
        // Re-fetch to get masked values back
        await fetchSettings();
        return true;
      }
      return false;
    } catch {
      return false;
    } finally {
      setSaving(false);
    }
  }, [fetchSettings]);

  const testConnection = useCallback(async (service: string, dto: AppSettings): Promise<TestResult> => {
    try {
      const res = await fetch(`${BACKEND_URL}/api/settings/test`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ service, settings: dto }),
      });
      if (res.ok) {
        return await res.json();
      }
      return { success: false, message: "Request failed" };
    } catch (err) {
      return { success: false, message: `Network error: ${err}` };
    }
  }, []);

  return {
    settings,
    health,
    loading,
    saving,
    fetchSettings,
    fetchHealth,
    saveSettings,
    testConnection,
  };
}
