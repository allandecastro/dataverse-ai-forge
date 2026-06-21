"use client";

import { useState, useCallback } from "react";
import { BACKEND_URL } from "@/lib/constants";

/** How the backend authenticates against Dataverse. */
export type DataverseAuthMode = "ServicePrincipal" | "Interactive";

export interface AppSettings {
  azureAiEndpoint: string;
  azureAiApiKey: string;
  azureAiDefaultModel: string;
  dataverseEnvironmentUrl: string;
  dataverseTenantId: string;
  dataverseClientId: string;
  dataverseClientSecret: string;
  /**
   * "ServicePrincipal" (TenantId + ClientId + ClientSecret) — works headless.
   * "Interactive" (TenantId + ClientId only) — opens a browser tab for sign-in
   * on the BACKEND machine. Fine for localhost dev only.
   */
  dataverseAuthMode: DataverseAuthMode;
}

export interface HealthStatus {
  status: string;
  dataverse?: { status: string; environment?: string; environmentUrl?: string; error?: string };
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
  dataverseEnvironmentUrl: "",
  dataverseTenantId: "",
  dataverseClientId: "",
  dataverseClientSecret: "",
  dataverseAuthMode: "ServicePrincipal",
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

  /**
   * Drop the backend's cached Dataverse credential. Used after the user wants to
   * switch accounts in Interactive mode — without this, the in-process token
   * cache would keep them signed in until a backend restart.
   */
  const signOutDataverse = useCallback(async (): Promise<TestResult> => {
    try {
      const res = await fetch(`${BACKEND_URL}/api/settings/dataverse/signout`, {
        method: "POST",
      });
      if (res.ok) {
        const body = await res.json();
        return { success: true, message: body?.message ?? "Signed out" };
      }
      return { success: false, message: "Sign-out request failed" };
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
    signOutDataverse,
  };
}
