"use client";

import { useState, useEffect, useCallback } from "react";
import { X, Brain, Database, Github, HardDrive, Activity, Loader2 } from "lucide-react";
import { useSettings, type AppSettings } from "@/hooks/useSettings";
import { useToast } from "@/contexts/ToastContext";
import { AzureAiSettings } from "./AzureAiSettings";
import { DataverseSettings } from "./DataverseSettings";
import { GitHubSettings } from "./GitHubSettings";
import { PersistenceSettings } from "./PersistenceSettings";
import { ObservabilitySettings } from "./ObservabilitySettings";
import type { LucideIcon } from "lucide-react";

type SectionId = "azureai" | "dataverse" | "github" | "persistence" | "observability";

interface SectionTab {
  id: SectionId;
  label: string;
  icon: LucideIcon;
}

const sections: SectionTab[] = [
  { id: "azureai", label: "Azure AI", icon: Brain },
  { id: "dataverse", label: "Dataverse", icon: Database },
  { id: "github", label: "GitHub", icon: Github },
  { id: "persistence", label: "Persistence", icon: HardDrive },
  { id: "observability", label: "Observability", icon: Activity },
];

const emptyForm: AppSettings = {
  azureAiEndpoint: "",
  azureAiApiKey: "",
  azureAiDefaultModel: "gpt-4o",
  dataverseEnvironmentUrl: "",
  dataverseTenantId: "",
  dataverseClientId: "",
  dataverseClientSecret: "",
  gitHubToken: "",
  persistenceProvider: "Sqlite",
  persistenceSqliteConnectionString: "Data Source=dataverse-forge.db",
  openTelemetryOtlpEndpoint: "",
};

interface SettingsModalProps {
  onClose: () => void;
}

export function SettingsModal({ onClose }: SettingsModalProps) {
  const { health, loading, saving, fetchSettings, fetchHealth, saveSettings, testConnection } = useSettings();
  const { addToast } = useToast();
  const [activeSection, setActiveSection] = useState<SectionId>("azureai");
  const [form, setForm] = useState<AppSettings | null>(null);
  const [testingService, setTestingService] = useState<string | null>(null);

  useEffect(() => {
    fetchSettings().then((data) => {
      setForm({ ...(data ?? emptyForm) });
    });
    fetchHealth();
  }, [fetchSettings, fetchHealth]);

  const handleChange = useCallback((patch: Partial<AppSettings>) => {
    setForm((prev) => (prev ? { ...prev, ...patch } : prev));
  }, []);

  const handleSave = useCallback(async () => {
    if (!form) return;
    const ok = await saveSettings(form);
    if (ok) {
      addToast("Settings saved successfully", "success");
      // Re-fetch to pick up fresh masked values + updated health status
      const fresh = await fetchSettings();
      if (fresh) setForm({ ...fresh });
      await fetchHealth();
    } else {
      addToast("Failed to save settings", "error");
    }
  }, [form, saveSettings, addToast, fetchSettings, fetchHealth]);

  const handleTest = useCallback(async (service: string) => {
    if (!form) return;
    setTestingService(service);
    const result = await testConnection(service, form);
    if (result.success) {
      addToast(result.message, "success");
    } else {
      addToast(result.message, "error");
    }
    setTestingService(null);
  }, [form, testConnection, addToast]);

  const getServiceStatus = (service: string): "connected" | "unavailable" | "unknown" => {
    if (!health) return "unknown";
    switch (service) {
      case "azureai":
        return health.azureAi?.status === "reachable" ? "connected" : health.azureAi ? "unavailable" : "unknown";
      case "dataverse":
        return health.dataverse?.status === "connected" ? "connected" : health.dataverse ? "unavailable" : "unknown";
      case "github":
        return health.github?.status === "connected" ? "connected" : health.github ? "unavailable" : "unknown";
      default:
        return "unknown";
    }
  };

  // Close on Escape key
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [onClose]);

  if (loading || !form) {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
        <div className="flex items-center gap-2 text-t-2">
          <Loader2 size={16} className="animate-spin" />
          Loading settings...
        </div>
      </div>
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
          <DataverseSettings
            settings={form}
            onChange={handleChange}
            status={getServiceStatus("dataverse")}
            testing={testingService === "dataverse"}
            onTest={() => handleTest("dataverse")}
          />
        );
      case "github":
        return (
          <GitHubSettings
            settings={form}
            onChange={handleChange}
            status={getServiceStatus("github")}
            testing={testingService === "github"}
            onTest={() => handleTest("github")}
          />
        );
      case "persistence":
        return <PersistenceSettings settings={form} onChange={handleChange} />;
      case "observability":
        return <ObservabilitySettings settings={form} onChange={handleChange} />;
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
      <div
        className="bg-bg-1 border border-border rounded-lg shadow-2xl flex flex-col overflow-hidden"
        style={{ width: 720, height: "80vh" }}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-3 border-b border-border">
          <h2 className="text-t-1 text-sm font-semibold">Settings</h2>
          <button
            onClick={onClose}
            className="text-t-3 hover:text-t-1 transition-colors"
          >
            <X size={16} />
          </button>
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
                      ? "bg-bg-2 text-t-1 border-r-2 border-blue"
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
          <button
            onClick={onClose}
            className="rounded border border-border bg-bg-2 px-4 py-1.5 text-xs text-t-2 hover:bg-bg-3 transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            disabled={saving}
            className="rounded bg-blue px-4 py-1.5 text-xs text-white hover:opacity-90 transition-opacity disabled:opacity-50 flex items-center gap-1.5"
          >
            {saving ? (
              <>
                <Loader2 size={12} className="animate-spin" />
                Saving...
              </>
            ) : (
              "Save Settings"
            )}
          </button>
        </div>
      </div>
    </div>
  );
}
