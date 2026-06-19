"use client";

import { useEffect, useId, useRef } from "react";
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
  const uid = useId();
  const titleId = `${uid}-title`;
  const messageId = `${uid}-message`;

  const cancelBtnRef = useRef<HTMLButtonElement | null>(null);

  // Stable callback ref so the Escape effect doesn't re-bind on every parent
  // re-render — inline `() => setOpen(false)` props are the common pattern
  // and would otherwise tear down + re-add the window listener each render.
  const onCancelRef = useRef(onCancel);
  useEffect(() => {
    onCancelRef.current = onCancel;
  }, [onCancel]);

  // Close on Escape.
  useEffect(() => {
    if (!open) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCancelRef.current();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [open]);

  // Focus management: move focus into the dialog on open (Cancel is the
  // safer default for destructive actions) and restore it on close. Without
  // this, keyboard users land on whatever was focused behind the overlay.
  useEffect(() => {
    if (!open) return;
    const previouslyFocused = document.activeElement as HTMLElement | null;
    cancelBtnRef.current?.focus();
    return () => {
      // Element may have unmounted while the dialog was open — guard the call.
      if (previouslyFocused && typeof previouslyFocused.focus === "function") {
        previouslyFocused.focus();
      }
    };
  }, [open]);

  if (!open) return null;

  return (
    // The overlay is purely visual; the actual modal semantics live on the
    // inner card so `aria-modal` correctly scopes assistive tech to it.
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={messageId}
        className="bg-bg-1 border border-border-hi p-5 w-full max-w-md mx-4"
        style={{ animation: "phase-fade-in 0.15s ease-out" }}
      >
        <div className="flex items-start gap-3 mb-3">
          {destructive && (
            <div className="flex-shrink-0 w-8 h-8 rounded-full bg-red-bg flex items-center justify-center">
              <AlertTriangle size={16} className="text-red" aria-hidden="true" />
            </div>
          )}
          <div className="flex-1 min-w-0">
            <h2 id={titleId} className="text-t-1 text-sm font-semibold mb-1">{title}</h2>
            <p id={messageId} className="text-t-3 text-xs leading-relaxed whitespace-pre-line">{message}</p>
          </div>
        </div>
        <div className="flex justify-end gap-2 mt-4">
          <button
            ref={cancelBtnRef}
            type="button"
            onClick={onCancel}
            className="px-3 py-1.5 rounded-md border border-border text-t-2 text-xs hover:bg-bg-2 transition-colors"
          >
            {cancelLabel}
          </button>
          <button
            type="button"
            onClick={onConfirm}
            className={`px-3 py-1.5 rounded-md text-xs font-medium transition-colors ${
              destructive
                ? "bg-red text-white hover:opacity-90"
                : "bg-accent text-accent-fg hover:opacity-90"
            }`}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
