"use client";

import { Database, User, Key, LogOut, Loader } from "lucide-react";
import { SettingsSection } from "./SettingsSection";
import { SettingsInput } from "./SettingsInput";
import type { AppSettings, DataverseAuthMode } from "@/hooks/useSettings";

interface DataverseSettingsProps {
  settings: AppSettings;
  onChange: (patch: Partial<AppSettings>) => void;
  status?: "connected" | "unavailable" | "unknown";
  testing: boolean;
  onTest: () => void;
  /** Only meaningful in Interactive mode — clears the backend's cached token
   * so the next Dataverse call re-prompts the browser sign-in. */
  onSignOut?: () => void;
  /** True while the sign-out request is in flight — disables the button so
   * the user can't double-click. */
  signingOut?: boolean;
}

const AUTH_MODES: Array<{
  value: DataverseAuthMode;
  label: string;
  hint: string;
  icon: typeof Key;
}> = [
  {
    value: "ServicePrincipal",
    label: "Service Principal",
    hint: "App Registration with a client secret. Works headless.",
    icon: Key,
  },
  {
    value: "Interactive",
    label: "Interactive sign-in",
    hint: "Sign in with your own account. No App Registration needed — uses Microsoft's public client.",
    icon: User,
  },
];

export function DataverseSettings({
  settings,
  onChange,
  status,
  testing,
  onTest,
  onSignOut,
  signingOut,
}: DataverseSettingsProps) {
  const isInteractive = settings.dataverseAuthMode === "Interactive";

  return (
    <SettingsSection
      icon={Database}
      title="Dataverse"
      status={status}
      testable
      testing={testing}
      onTest={onTest}
    >
      <div className="flex flex-col gap-2">
        <label className="text-xs text-t-3">Authentication</label>
        <div className="grid grid-cols-2 gap-2">
          {AUTH_MODES.map(({ value, label, hint, icon: Icon }) => {
            const active = settings.dataverseAuthMode === value;
            return (
              <button
                key={value}
                type="button"
                onClick={() => onChange({ dataverseAuthMode: value })}
                className={`flex flex-col gap-1 rounded-lg border px-3 py-2.5 text-left transition-colors ${
                  active
                    ? "border-blue/60 bg-blue-bg/30"
                    : "border-border bg-bg-2 hover:border-blue/30 hover:bg-bg-3"
                }`}
              >
                <div className="flex items-center gap-1.5">
                  <Icon size={12} className={active ? "text-blue" : "text-t-3"} />
                  <span className={`text-xs font-medium ${active ? "text-t-1" : "text-t-2"}`}>
                    {label}
                  </span>
                </div>
                <span className="text-t-4" style={{ fontSize: 11, lineHeight: 1.3 }}>
                  {hint}
                </span>
              </button>
            );
          })}
        </div>
      </div>

      <SettingsInput
        label="Environment URL"
        value={settings.dataverseEnvironmentUrl}
        onChange={(v) => onChange({ dataverseEnvironmentUrl: v })}
        placeholder="https://your-org.crm.dynamics.com"
        monospace
      />
      <SettingsInput
        label={isInteractive ? "Tenant ID (optional)" : "Tenant ID"}
        value={settings.dataverseTenantId}
        onChange={(v) => onChange({ dataverseTenantId: v })}
        placeholder={
          isInteractive
            ? "Leave blank — pick during sign-in"
            : "00000000-0000-0000-0000-000000000000"
        }
        monospace
      />
      <SettingsInput
        label={isInteractive ? "Client ID (optional)" : "Client ID"}
        value={settings.dataverseClientId}
        onChange={(v) => onChange({ dataverseClientId: v })}
        placeholder={
          isInteractive
            ? "Leave blank — uses Microsoft's public client"
            : "00000000-0000-0000-0000-000000000000"
        }
        monospace
      />

      {!isInteractive && (
        <SettingsInput
          label="Client Secret"
          value={settings.dataverseClientSecret}
          onChange={(v) => onChange({ dataverseClientSecret: v })}
          placeholder="Enter client secret"
          type="password"
          monospace
        />
      )}

      {isInteractive && (
        <>
          <div className="rounded-md border border-border bg-bg-2 px-3 py-2 text-xs text-t-3 leading-relaxed">
            <p className="mb-1">
              <strong className="text-t-2">No setup needed.</strong> Leave Tenant ID and
              Client ID blank to use Microsoft&apos;s public Power Platform client (
              <code className="text-t-2 font-mono">
                51f81489-12ee-4a9e-aaae-a2591f45987d
              </code>
              ). You just need a Dataverse user account in the target environment.
            </p>
            <p>
              <strong className="text-t-2">Heads up:</strong> The browser tab opens on the
              <em> backend</em>&apos;s machine, not the frontend&apos;s. Works for localhost
              dev only.
            </p>
          </div>
          {onSignOut && (
            <button
              type="button"
              onClick={onSignOut}
              disabled={signingOut}
              className="flex items-center justify-center gap-1.5 rounded-md border border-border bg-bg-2 px-3 py-1.5 text-xs text-t-2 hover:bg-bg-3 hover:border-orange/40 disabled:opacity-50 disabled:cursor-not-allowed transition-colors w-fit"
              title="Clear the cached token so the next Dataverse call re-prompts the browser sign-in"
            >
              {signingOut ? (
                <Loader size={12} className="animate-spin" />
              ) : (
                <LogOut size={12} />
              )}
              {signingOut ? "Signing out…" : "Sign out (clear cached token)"}
            </button>
          )}
        </>
      )}
    </SettingsSection>
  );
}
