"use client";

import { Database } from "lucide-react";
import { SettingsSection } from "./SettingsSection";
import { SettingsInput } from "./SettingsInput";
import type { AppSettings } from "@/hooks/useSettings";

interface DataverseSettingsProps {
  settings: AppSettings;
  onChange: (patch: Partial<AppSettings>) => void;
  status?: "connected" | "unavailable" | "unknown";
  testing: boolean;
  onTest: () => void;
}

export function DataverseSettings({
  settings,
  onChange,
  status,
  testing,
  onTest,
}: DataverseSettingsProps) {
  return (
    <SettingsSection
      icon={Database}
      title="Dataverse"
      status={status}
      testable
      testing={testing}
      onTest={onTest}
    >
      <SettingsInput
        label="Environment URL"
        value={settings.dataverseEnvironmentUrl}
        onChange={(v) => onChange({ dataverseEnvironmentUrl: v })}
        placeholder="https://your-org.crm.dynamics.com"
        monospace
      />
      <SettingsInput
        label="Tenant ID"
        value={settings.dataverseTenantId}
        onChange={(v) => onChange({ dataverseTenantId: v })}
        placeholder="00000000-0000-0000-0000-000000000000"
        monospace
      />
      <SettingsInput
        label="Client ID"
        value={settings.dataverseClientId}
        onChange={(v) => onChange({ dataverseClientId: v })}
        placeholder="00000000-0000-0000-0000-000000000000"
        monospace
      />
      <SettingsInput
        label="Client Secret"
        value={settings.dataverseClientSecret}
        onChange={(v) => onChange({ dataverseClientSecret: v })}
        placeholder="Enter client secret"
        type="password"
        monospace
      />
    </SettingsSection>
  );
}
