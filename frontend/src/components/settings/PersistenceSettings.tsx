"use client";

import { HardDrive } from "lucide-react";
import { SettingsSection } from "./SettingsSection";
import { SettingsInput } from "./SettingsInput";
import type { AppSettings } from "@/hooks/useSettings";

interface PersistenceSettingsProps {
  settings: AppSettings;
  onChange: (patch: Partial<AppSettings>) => void;
}

export function PersistenceSettings({
  settings,
  onChange,
}: PersistenceSettingsProps) {
  return (
    <SettingsSection
      icon={HardDrive}
      title="Persistence"
      requiresRestart
    >
      <div className="space-y-1">
        <label className="block text-t-2 text-xs font-medium">Provider</label>
        <select
          value={settings.persistenceProvider}
          onChange={(e) => onChange({ persistenceProvider: e.target.value })}
          className="w-full rounded border border-border bg-bg-2 px-3 py-1.5 text-t-1 text-sm focus:border-blue focus:outline-none"
        >
          <option value="InMemory">In-Memory</option>
          <option value="Sqlite">SQLite</option>
        </select>
      </div>
      {settings.persistenceProvider === "Sqlite" && (
        <SettingsInput
          label="SQLite Connection String"
          value={settings.persistenceSqliteConnectionString}
          onChange={(v) => onChange({ persistenceSqliteConnectionString: v })}
          placeholder="Data Source=dataverse-forge.db"
          monospace
        />
      )}
    </SettingsSection>
  );
}
