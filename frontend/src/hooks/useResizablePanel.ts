"use client";

import { useCallback, useState } from "react";

interface UseResizablePanelOptions {
  initialWidth?: number;
  minWidth?: number;
  /** Hard cap; the panel is also clamped to 90vw at render time via maxWidth. */
  maxWidth?: number;
}

/**
 * Drag-to-resize state for a left-anchored panel whose right edge is the handle.
 * Supports both mouse and touch pointers (the panel was previously mouse-only,
 * so it couldn't be resized on touch devices). Returns the current width plus a
 * spread-ready set of handle props.
 */
export function useResizablePanel({
  initialWidth = 420,
  minWidth = 200,
  maxWidth = 900,
}: UseResizablePanelOptions = {}) {
  const [width, setWidth] = useState(initialWidth);

  const startResize = useCallback(
    (clientX: number) => {
      const startX = clientX;
      const startW = width;

      const apply = (currentX: number) => {
        const next = Math.min(maxWidth, Math.max(minWidth, startW + (currentX - startX)));
        setWidth(next);
      };

      const onMouseMove = (e: MouseEvent) => apply(e.clientX);
      const onTouchMove = (e: TouchEvent) => {
        if (e.touches.length > 0) apply(e.touches[0].clientX);
      };
      const cleanup = () => {
        document.removeEventListener("mousemove", onMouseMove);
        document.removeEventListener("mouseup", cleanup);
        document.removeEventListener("touchmove", onTouchMove);
        document.removeEventListener("touchend", cleanup);
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
      };

      document.addEventListener("mousemove", onMouseMove);
      document.addEventListener("mouseup", cleanup);
      document.addEventListener("touchmove", onTouchMove, { passive: true });
      document.addEventListener("touchend", cleanup);
      document.body.style.cursor = "col-resize";
      document.body.style.userSelect = "none";
    },
    [width, minWidth, maxWidth],
  );

  const handleProps = {
    onMouseDown: (e: React.MouseEvent) => {
      e.preventDefault();
      startResize(e.clientX);
    },
    onTouchStart: (e: React.TouchEvent) => {
      if (e.touches.length > 0) startResize(e.touches[0].clientX);
    },
  };

  return { width, setWidth, handleProps };
}
