"use client";

type Status = "connected" | "unavailable" | "unknown";

interface ConnectionStatusProps {
  status: Status;
  label?: string;
}

const statusColors: Record<Status, string> = {
  connected: "bg-green",
  unavailable: "bg-red",
  unknown: "bg-t-4",
};

const statusLabels: Record<Status, string> = {
  connected: "Connected",
  unavailable: "Unavailable",
  unknown: "Unknown",
};

export function ConnectionStatus({ status, label }: ConnectionStatusProps) {
  return (
    <div className="flex items-center gap-1.5">
      <div
        className={`w-2 h-2 rounded-full ${statusColors[status]}`}
      />
      <span className="text-t-3 text-xs">{label || statusLabels[status]}</span>
    </div>
  );
}
