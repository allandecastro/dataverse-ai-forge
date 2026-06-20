"use client";

import { useEffect, useRef } from "react";

/**
 * Cross-cutting behaviour every overlay (modal, slide-over, popover) needs:
 *  - Escape closes it.
 *  - Focus is restored to whatever was focused before it opened, on unmount.
 *
 * Extracted so `ModalOverlay` (centered dialogs) and `DiagPanel` (right
 * slide-over drawer) share ONE implementation instead of each hand-rolling
 * keyboard + focus handling — or, in DiagPanel's case, omitting it entirely.
 * The layout/scrim is left to each overlay since those legitimately differ
 * (centered vs edge-anchored, /50 vs /30 tint).
 */
export function useOverlayDismiss(onClose?: () => void): void {
  // Ref so the keydown listener subscribes once and never re-binds when the
  // caller passes a fresh onClose each render.
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

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null;
    return () => {
      if (previouslyFocused && typeof previouslyFocused.focus === "function") {
        previouslyFocused.focus();
      }
    };
  }, []);
}
