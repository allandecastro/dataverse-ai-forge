"use client";

interface SettingsInputProps {
  label: string;
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  monospace?: boolean;
  description?: string;
  type?: "text" | "password";
  /** Read-only (e.g. a project's environment URL once it is set). */
  disabled?: boolean;
}

export function SettingsInput({
  label,
  value,
  onChange,
  placeholder,
  monospace,
  description,
  type = "text",
  disabled,
}: SettingsInputProps) {
  return (
    <div className="space-y-1">
      <label className="block text-t-2 text-xs font-medium">{label}</label>
      {description && (
        <p className="text-t-4 text-xs">{description}</p>
      )}
      <input
        type={type}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        disabled={disabled}
        className={`w-full rounded border border-border bg-bg-2 px-3 py-1.5 text-t-1 text-sm placeholder:text-t-4 focus:border-accent-ink focus:outline-none disabled:opacity-60 disabled:cursor-not-allowed ${
          monospace ? "font-mono" : ""
        }`}
      />
    </div>
  );
}
