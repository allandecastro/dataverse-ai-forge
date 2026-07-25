"use client";

import { useEffect, useRef, type RefObject } from "react";

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Trap Tab / Shift+Tab within the returned container so keyboard users can't
 * wander into the (inert) page behind an open overlay. Pairs with
 * `useOverlayDismiss` (Escape-to-close + restore-focus-on-unmount), which
 * deliberately leaves containment to each overlay — this is that piece, shared
 * by the centered ModalOverlay and the edge-anchored DiagPanel slide-over.
 *
 * Focusables are re-queried per keypress so dynamically-revealed controls
 * (expanded Details, code tabs, filtered lists) are included, and hidden nodes
 * (offsetParent === null) are skipped.
 */
export function useFocusTrap<T extends HTMLElement>(): RefObject<T | null> {
  const ref = useRef<T>(null);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key !== "Tab") return;
      const focusables = Array.from(
        el.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      ).filter((n) => n.offsetParent !== null);
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
  return ref;
}
