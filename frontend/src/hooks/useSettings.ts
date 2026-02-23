"use client";

import { useState, useCallback } from "react";

const BACKEND_URL = process.env.NEXT_PUBLIC_BACKEND_URL || "http://localhost:5000";

export interface AppSettings {
  azureOpenAiEndpoint: string;
  azureOpenAiApiKey: string;
  azureOpenAiGpt4oDeploymentName: string;
  azureOpenAiGpt4oMiniDeploymentName: string;
  dataverseEnvironmentUrl: string;
  dataverseTenantId: string;
  dataverseClientId: string;
  dataverseClientSecret: string;
  gitHubToken: string;
  persistenceProvider: string;
  persistenceSqliteConnectionString: string;
  openTelemetryOtlpEndpoint: string;
}

export interface HealthStatus {
  status: string;
  dataverse?: { status: string; environment?: string; error?: string };
  azureOpenAi?: { status: string; endpoint?: string };
  github?: { status: string };
}

export interface TestResult {
  success: boolean;
  message: string;
}

const emptySettings: AppSettings = {
  azureOpenAiEndpoint: "",
  azureOpenAiApiKey: "",
  azureOpenAiGpt4oDeploymentName: "",
  azureOpenAiGpt4oMiniDeploymentName: "",
  dataverseEnvironmentUrl: "",
  dataverseTenantId: "",
  dataverseClientId: "",
  dataverseClientSecret: "",
  gitHubToken: "",
  persistenceProvider: "",
  persistenceSqliteConnectionString: "",
  openTelemetryOtlpEndpoint: "",
};

export function useSettings() {
  const [settings, setSettings] = useState<AppSettings>(emptySettings);
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);

  const fetchSettings = useCallback(async () => {
    setLoading(true);
    try {
      const res = await fetch(`${BACKEND_URL}/api/settings`);
      if (res.ok) {
        const data = await res.json();
        setSettings(data);
      }
    } catch {
      // Backend may not be running
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchHealth = useCallback(async () => {
    try {
      const res = await fetch(`${BACKEND_URL}/healthz`);
      if (res.ok) {
        const data = await res.json();
        setHealth(data);
      }
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
