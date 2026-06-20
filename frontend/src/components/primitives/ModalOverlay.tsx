"use client";

import { useEffect, useRef, type ReactNode } from "react";
import { cn } from "@/lib/cn";

interface ModalOverlayProps {
  children: ReactNode;
  /** Called on Escape and (when enabled) backdrop click. */
  onClose?: () => void;
  /** Close when the backdrop (not the content) is clicked. Default: true. */
  closeOnBackdrop?: boolean;
  /** Tailwind z-index utility for the backdrop. Default: z-50. */
  zClassName?: string;
  /** Extra classes for the backdrop (e.g. a darker tint). */
  className?: string;
}

/**
 * Centered modal backdrop with the cross-cutting concerns every dialog needs:
 * Escape-to-close, restore-focus-on-unmount, and responsive edge padding (`p-4`)
 * so inner cards using `max-w-*` never bleed off a narrow viewport.
 *
 * Replaces the hand-rolled `fixed inset-0 … bg-black/…` backdrop that was copied
 * across ~7 call sites with drifting z-index/opacity and no keyboard handling.
 * The inner card still owns its own `role="dialog" aria-modal` + sizing.
 */
export function ModalOverlay({
  children,
  onClose,
  closeOnBackdrop = true,
  zClassName = "z-50",
  className,
}: ModalOverlayProps) {
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCloseRef.current?.();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, []);

  // Restore focus to whatever was focused before the modal opened.
  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null;
    return () => {
      if (previouslyFocused && typeof previouslyFocused.focus === "function") {
        previouslyFocused.focus();
      }
    };
  }, []);

  return (
    <div
      className={cn("app-overlay fixed inset-0 flex items-center justify-center bg-black/50 p-4", zClassName, className)}
      onClick={closeOnBackdrop ? (e) => { if (e.target === e.currentTarget) onClose?.(); } : undefined}
    >
      {children}
    </div>
  );
}
