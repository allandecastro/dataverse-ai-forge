"use client";

interface SettingsInputProps {
  label: string;
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  monospace?: boolean;
  description?: string;
  type?: "text" | "password";
}

export function SettingsInput({
  label,
  value,
  onChange,
  placeholder,
  monospace,
  description,
  type = "text",
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
        className={`w-full rounded border border-border bg-bg-2 px-3 py-1.5 text-t-1 text-sm placeholder:text-t-4 focus:border-blue focus:outline-none ${
          monospace ? "font-mono" : ""
        }`}
      />
    </div>
  );
}
