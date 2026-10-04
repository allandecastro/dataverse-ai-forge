"use client";

import { useState, useCallback } from "react";
import { BACKEND_URL } from "@/lib/constants";
import type { DataverseAuthMode, TestResult } from "@/hooks/useSettings";

/**
 * A project's own Dataverse / Azure DevOps connection. Each project targets its own
 * environment, so these credentials live on the project, not in the global settings.
 * Secrets come back masked; sending a masked value back keeps the stored one.
 */
export interface ProjectConnection {
  dataverseEnvironmentUrl: string;
  dataverseAuthMode: DataverseAuthMode;
  dataverseTenantId: string;
  dataverseClientId: string;
  dataverseClientSecret: string;
  /** True once an environment URL is saved — it can no longer change. */
  dataverseEnvironmentLocked: boolean;
  azureDevOpsOrganizationUrl: string;
  azureDevOpsProject: string;
  azureDevOpsPersonalAccessToken: string;
}

export const emptyProjectConnection: ProjectConnection = {
  dataverseEnvironmentUrl: "",
  dataverseAuthMode: "ServicePrincipal",
  dataverseTenantId: "",
  dataverseClientId: "",
  dataverseClientSecret: "",
  dataverseEnvironmentLocked: false,
  azureDevOpsOrganizationUrl: "",
  azureDevOpsProject: "",
  azureDevOpsPersonalAccessToken: "",
};

async function errorMessage(res: Response, fallback: string): Promise<string> {
  try {
    const body = await res.json();
    return body?.error ?? body?.message ?? fallback;
  } catch {
    return fallback;
  }
}

export function useProjectConnection(sessionId: string | undefined) {
  const [saving, setSaving] = useState(false);
  const base = sessionId ? `${BACKEND_URL}/api/sessions/${encodeURIComponent(sessionId)}/connection` : null;

  const fetchConnection = useCallback(async (): Promise<ProjectConnection | null> => {
    if (!base) return null;
    try {
      const res = await fetch(base);
      return res.ok ? ((await res.json()) as ProjectConnection) : null;
    } catch {
      return null;
    }
  }, [base]);

  /** Saves the connection. Returns the saved (masked) value, or an error message. */
  const saveConnection = useCallback(
    async (dto: ProjectConnection): Promise<{ connection?: ProjectConnection; error?: string }> => {
      if (!base) return { error: "No project selected." };
      setSaving(true);
      try {
        const res = await fetch(base, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(dto),
        });
        if (res.ok) return { connection: (await res.json()) as ProjectConnection };
        return { error: await errorMessage(res, "Failed to save the project connection") };
      } catch (err) {
        return { error: `Network error: ${err}` };
      } finally {
        setSaving(false);
      }
    },
    [base],
  );

  /** Tests the (possibly unsaved) values in the form. */
  const testConnection = useCallback(
    async (dto: ProjectConnection): Promise<TestResult> => {
      if (!base) return { success: false, message: "No project selected." };
      try {
        const res = await fetch(`${base}/test`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(dto),
        });
        if (res.ok) return (await res.json()) as TestResult;
        return { success: false, message: await errorMessage(res, "Request failed") };
      } catch (err) {
        return { success: false, message: `Network error: ${err}` };
      }
    },
    [base],
  );

  /** Drops this project's cached Dataverse credential (Interactive: re-prompts sign-in). */
  const signOut = useCallback(async (): Promise<TestResult> => {
    if (!base) return { success: false, message: "No project selected." };
    try {
      const res = await fetch(`${base}/signout`, { method: "POST" });
      if (res.ok) {
        const body = await res.json();
        return { success: true, message: body?.message ?? "Signed out" };
      }
      return { success: false, message: "Sign-out request failed" };
    } catch (err) {
      return { success: false, message: `Network error: ${err}` };
    }
  }, [base]);

  return { saving, fetchConnection, saveConnection, testConnection, signOut };
}
