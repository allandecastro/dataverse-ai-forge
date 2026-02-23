"use client";

import { Activity } from "lucide-react";
import { SettingsSection } from "./SettingsSection";
import { SettingsInput } from "./SettingsInput";
import type { AppSettings } from "@/hooks/useSettings";

interface ObservabilitySettingsProps {
  settings: AppSettings;
  onChange: (patch: Partial<AppSettings>) => void;
}

export function ObservabilitySettings({
  settings,
  onChange,
}: ObservabilitySettingsProps) {
  return (
    <SettingsSection
      icon={Activity}
      title="Observability"
      requiresRestart
    >
      <SettingsInput
        label="OTLP Endpoint"
        value={settings.openTelemetryOtlpEndpoint}
        onChange={(v) => onChange({ openTelemetryOtlpEndpoint: v })}
        placeholder="http://localhost:4317"
        monospace
        description="OpenTelemetry collector endpoint for traces and metrics."
      />
    </SettingsSection>
  );
}
