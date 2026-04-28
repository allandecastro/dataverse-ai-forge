"use client";

import { useEffect } from "react";
import { AlertTriangle } from "lucide-react";

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  destructive = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  // Close on Escape
  useEffect(() => {
    if (!open) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCancel();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [open, onCancel]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50">
      <div
        className="bg-bg-1 border border-border rounded-lg shadow-xl p-5 w-full max-w-md mx-4"
        style={{ animation: "phase-fade-in 0.15s ease-out" }}
      >
        <div className="flex items-start gap-3 mb-3">
          {destructive && (
            <div className="flex-shrink-0 w-8 h-8 rounded-full bg-red-bg flex items-center justify-center">
              <AlertTriangle size={16} className="text-red" />
            </div>
          )}
          <div className="flex-1 min-w-0">
            <h2 className="text-t-1 text-sm font-semibold mb-1">{title}</h2>
            <p className="text-t-3 text-xs leading-relaxed whitespace-pre-line">{message}</p>
          </div>
        </div>
        <div className="flex justify-end gap-2 mt-4">
          <button
            onClick={onCancel}
            className="px-3 py-1.5 rounded-md border border-border text-t-2 text-xs hover:bg-bg-2 transition-colors"
          >
            {cancelLabel}
          </button>
          <button
            onClick={onConfirm}
            className={`px-3 py-1.5 rounded-md text-xs font-medium transition-colors ${
              destructive
                ? "bg-red text-white hover:opacity-90"
                : "bg-blue text-white hover:opacity-90"
            }`}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
