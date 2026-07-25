"use client";

import { useEffect, useRef, type ReactNode } from "react";
import { cn } from "@/lib/cn";
import { useOverlayDismiss } from "@/hooks/useOverlayDismiss";

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

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
  // Escape-to-close + restore-focus-on-unmount (shared with DiagPanel).
  useOverlayDismiss(onClose);

  // Trap Tab / Shift+Tab within the modal so keyboard users can't wander into
  // the (inert) page behind it. Escape + restore-focus are handled above; this
  // adds the containment that useOverlayDismiss deliberately leaves to each
  // overlay. Focusables are re-queried per keypress so dynamically-revealed
  // controls (expanded Details, code tabs) are included.
  const backdropRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = backdropRef.current;
    if (!el) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key !== "Tab") return;
      const focusables = Array.from(
        el.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      ).filter((n) => n.offsetParent !== null); // skip hidden nodes
      if (focusables.length === 0) return;
      const first = focusables[0];
      const last = focusables[focusables.length - 1];
      const active = document.activeElement as HTMLElement | null;
      if (e.shiftKey && (active === first || !el.contains(active))) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && active === last) {
        e.preventDefault();
        first.focus();
      }
    };
    el.addEventListener("keydown", handler);
    return () => el.removeEventListener("keydown", handler);
  }, []);

  return (
    <div
      ref={backdropRef}
      className={cn("app-overlay fixed inset-0 flex items-center justify-center bg-black/50 p-4", zClassName, className)}
      onClick={closeOnBackdrop ? (e) => { if (e.target === e.currentTarget) onClose?.(); } : undefined}
    >
      {children}
    </div>
  );
}
