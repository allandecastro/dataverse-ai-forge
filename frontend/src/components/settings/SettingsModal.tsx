"use client";

import { useState, useEffect, useCallback, useRef } from "react";
import { X, Brain, Database, HardDrive, Loader2 } from "lucide-react";
import { useSettings, type AppSettings } from "@/hooks/useSettings";
import { useProjectConnection, emptyProjectConnection, type ProjectConnection } from "@/hooks/useProjectConnection";
import { useToast } from "@/contexts/ToastContext";
import { AzureAiSettings } from "./AzureAiSettings";
import { DataverseSettings } from "./DataverseSettings";
import { AzureDevOpsSettings } from "./AzureDevOpsSettings";
import { StorageSettings } from "./StorageSettings";
import { ModalOverlay } from "@/components/primitives/ModalOverlay";
import { Button } from "@/components/primitives/Button";
import { Tooltip } from "@/components/primitives/Tooltip";
import type { LucideIcon } from "lucide-react";

export type SectionId = "azureai" | "dataverse" | "storage";

interface SectionTab {
  id: SectionId;
  label: string;
  icon: LucideIcon;
}

const allSections: SectionTab[] = [
  { id: "azureai", label: "Azure AI", icon: Brain },
  // Per project: the open project's Dataverse (+ Azure DevOps) connection.
  { id: "dataverse", label: "This project", icon: Database },
  { id: "storage", label: "Data & storage", icon: HardDrive },
];

const emptyForm: AppSettings = {
  azureAiEndpoint: "",
  azureAiApiKey: "",
  azureAiDefaultModel: "gpt-4o",
  azureAiAgentModels: {},
  ragEmbeddingDeployment: "",
};

interface SettingsModalProps {
  onClose: () => void;
  /** The open project. Without one (demo), the per-project tab is hidden. */
  sessionId?: string;
  projectName?: string;
  /** Tab to open on, e.g. "dataverse" from the "no connection" banner. */
  initialSection?: SectionId;
}

export function SettingsModal({ onClose, sessionId, projectName, initialSection }: SettingsModalProps) {
  const { health, loading, saving, fetchSettings, fetchHealth, saveSettings, testConnection } = useSettings();
  const project = useProjectConnection(sessionId);
  const { addToast } = useToast();
  const sections = sessionId ? allSections : allSections.filter((s) => s.id !== "dataverse");
  const [activeSection, setActiveSection] = useState<SectionId>(initialSection ?? "azureai");
  const [form, setForm] = useState<AppSettings | null>(null);
  const [connection, setConnection] = useState<ProjectConnection>(emptyProjectConnection);
  const [connectionDirty, setConnectionDirty] = useState(false);
  const [testingService, setTestingService] = useState<string | null>(null);

  const { fetchConnection } = project;
  useEffect(() => {
    fetchSettings().then((data) => {
      setForm({ ...(data ?? emptyForm) });
    });
    fetchConnection().then((c) => {
      if (c) setConnection(c);
    });
    fetchHealth(sessionId);
  }, [fetchSettings, fetchConnection, fetchHealth, sessionId]);

  const handleChange = useCallback((patch: Partial<AppSettings>) => {
    setForm((prev) => (prev ? { ...prev, ...patch } : prev));
  }, []);

  const handleConnectionChange = useCallback((patch: Partial<ProjectConnection>) => {
    setConnection((prev) => ({ ...prev, ...patch }));
    setConnectionDirty(true);
  }, []);

  const handleSave = useCallback(async () => {
    if (!form) return;
    const ok = await saveSettings(form);
    if (!ok) {
      addToast("Failed to save settings", "error");
      return;
    }
    if (sessionId && connectionDirty) {
      const { connection: saved, error } = await project.saveConnection(connection);
      if (error) {
        addToast(error, "error");
        setActiveSection("dataverse");
        return;
      }
      if (saved) setConnection(saved);
      setConnectionDirty(false);
    }
    addToast("Settings saved successfully", "success");
    // Re-fetch to pick up fresh masked values + updated health status
    const fresh = await fetchSettings();
    if (fresh) setForm({ ...fresh });
    await fetchHealth(sessionId);
  }, [form, saveSettings, addToast, fetchSettings, fetchHealth, sessionId, connectionDirty, connection, project]);

  const handleTest = useCallback(async (service: string) => {
    if (!form) return;
    setTestingService(service);
    const result = service === "dataverse"
      ? await project.testConnection(connection)
      : await testConnection(service, form);
    if (result.success) {
      addToast(result.message, "success");
    } else {
      addToast(result.message, "error");
    }
    setTestingService(null);
  }, [form, testConnection, addToast, project, connection]);

  // Focus the dialog once when it first mounts (loaded branch) so keyboard/SR
  // users land inside the modal instead of leaving focus on the trigger. A
  // callback ref guarded by a one-shot flag avoids re-stealing focus on every
  // keystroke (which a `useEffect([form])` would do as the form state changes).
  const focusedRef = useRef(false);
  const dialogRef = useCallback((node: HTMLDivElement | null) => {
    if (node && !focusedRef.current) {
      focusedRef.current = true;
      node.focus();
    }
  }, []);

  const [signingOutDataverse, setSigningOutDataverse] = useState(false);
  const handleSignOutDataverse = useCallback(async () => {
    setSigningOutDataverse(true);
    try {
      const result = await project.signOut();
      addToast(result.message, result.success ? "success" : "error");
      // Re-probe health so the sidebar status reflects the cleared session.
      if (result.success) void fetchHealth(sessionId);
    } finally {
      setSigningOutDataverse(false);
    }
  }, [project, addToast, fetchHealth, sessionId]);

  const getServiceStatus = (service: string): "connected" | "unavailable" | "unknown" => {
    if (!health) return "unknown";
    switch (service) {
      case "azureai":
        return health.azureAi?.status === "reachable" ? "connected" : health.azureAi ? "unavailable" : "unknown";
      case "dataverse":
        return health.dataverse?.status === "connected" ? "connected" : health.dataverse ? "unavailable" : "unknown";
      default:
        return "unknown";
    }
  };

  if (loading || !form) {
    return (
      <ModalOverlay onClose={onClose} closeOnBackdrop={false}>
        <div className="flex items-center gap-2 text-t-2">
          <Loader2 size={16} className="animate-spin" />
          Loading settings...
        </div>
      </ModalOverlay>
    );
  }

  function renderSection() {
    if (!form) return null;
    switch (activeSection) {
      case "azureai":
        return (
          <AzureAiSettings
            settings={form}
            onChange={handleChange}
            status={getServiceStatus("azureai")}
            testing={testingService === "azureai"}
            onTest={() => handleTest("azureai")}
          />
        );
      case "dataverse":
        return (
          <div className="flex flex-col gap-6">
            <DataverseSettings
              settings={connection}
              onChange={handleConnectionChange}
              projectName={projectName}
              status={getServiceStatus("dataverse")}
              testing={testingService === "dataverse"}
              onTest={() => handleTest("dataverse")}
              onSignOut={handleSignOutDataverse}
              signingOut={signingOutDataverse}
            />
            <AzureDevOpsSettings settings={connection} onChange={handleConnectionChange} />
          </div>
        );
      case "storage":
        return <StorageSettings />;
    }
  }

  return (
    <ModalOverlay onClose={onClose} closeOnBackdrop={false}>
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="settings-modal-title"
        tabIndex={-1}
        className="bg-bg-1 border border-border-hi flex flex-col overflow-hidden w-full max-w-[720px] h-[80vh] outline-none"
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-3 border-b border-border">
          <h2 id="settings-modal-title" className="text-t-1 text-sm font-semibold">Settings</h2>
          <Tooltip label="Close">
            <button
              onClick={onClose}
              aria-label="Close settings"
              className="text-t-3 hover:text-t-1 transition-colors"
            >
              <X size={16} />
            </button>
          </Tooltip>
        </div>

        {/* Body */}
        <div className="flex flex-1 min-h-0">
          {/* Section tabs */}
          <div className="w-48 border-r border-border bg-bg-0 py-2 flex-shrink-0 overflow-y-auto">
            {sections.map((s) => {
              const Icon = s.icon;
              const active = activeSection === s.id;
              return (
                <button
                  key={s.id}
                  onClick={() => setActiveSection(s.id)}
                  className={`w-full flex items-center gap-2 px-4 py-2 text-xs transition-colors ${
                    active
                      ? "bg-bg-2 text-t-1 border-r-2 border-accent"
                      : "text-t-3 hover:text-t-2 hover:bg-bg-1"
                  }`}
                >
                  <Icon size={14} />
                  {s.label}
                </button>
              );
            })}
          </div>

          {/* Active section */}
          <div className="flex-1 p-5 overflow-y-auto">
            {renderSection()}
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-2 px-5 py-3 border-t border-border">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button variant="primary" onClick={handleSave} disabled={saving}>
            {saving ? (
              <>
                <Loader2 size={12} className="animate-spin" />
                Saving...
              </>
            ) : (
              "Save Settings"
            )}
          </Button>
        </div>
      </div>
    </ModalOverlay>
  );
}
