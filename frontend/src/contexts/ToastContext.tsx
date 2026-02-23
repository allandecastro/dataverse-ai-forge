"use client";

import { createContext, useContext, useState, useCallback, useRef, type ReactNode } from "react";
import { X } from "lucide-react";

type ToastType = "error" | "success" | "info";

interface Toast {
  id: string;
  message: string;
  type: ToastType;
  timestamp: number;
}

interface ToastContextValue {
  addToast: (message: string, type: ToastType) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function useToast() {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error("useToast must be used within ToastProvider");
  return ctx;
}

const DISMISS_DELAY = 6_000;

const borderColors: Record<ToastType, string> = {
  error: "border-l-red",
  success: "border-l-green",
  info: "border-l-blue",
};

const bgColors: Record<ToastType, string> = {
  error: "bg-red-bg",
  success: "bg-green-bg",
  info: "bg-blue-bg",
};

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const counterRef = useRef(0);

  const removeToast = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const addToast = useCallback(
    (message: string, type: ToastType) => {
      const id = `toast-${++counterRef.current}`;
      const toast: Toast = { id, message, type, timestamp: Date.now() };
      setToasts((prev) => [...prev, toast]);
      setTimeout(() => removeToast(id), DISMISS_DELAY);
    },
    [removeToast],
  );

  return (
    <ToastContext.Provider value={{ addToast }}>
      {children}
      {/* Toast container */}
      <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm">
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className={`border-l-4 ${borderColors[toast.type]} ${bgColors[toast.type]} bg-bg-2 rounded px-4 py-3 shadow-lg flex items-start gap-3`}
            style={{ animation: "toast-slide-in 0.3s ease-out" }}
          >
            <span className="text-t-1 text-sm flex-1 break-words">{toast.message}</span>
            <button
              onClick={() => removeToast(toast.id)}
              className="text-t-3 hover:text-t-1 transition-colors flex-shrink-0"
            >
              <X size={14} />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}
