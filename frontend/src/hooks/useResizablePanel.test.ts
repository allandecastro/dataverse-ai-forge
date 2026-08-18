import { describe, it, expect, afterEach } from "vitest";
import { renderHook, act, cleanup } from "@testing-library/react";
import { useResizablePanel } from "./useResizablePanel";

afterEach(() => cleanup());

function mouseMove(clientX: number) {
  const evt = new MouseEvent("mousemove");
  Object.defineProperty(evt, "clientX", { value: clientX });
  document.dispatchEvent(evt);
}

function startDrag(onMouseDown: (e: React.MouseEvent) => void, clientX: number) {
  onMouseDown({ clientX, preventDefault() {} } as unknown as React.MouseEvent);
}

describe("useResizablePanel", () => {
  it("starts at the initial width", () => {
    const { result } = renderHook(() => useResizablePanel({ initialWidth: 420 }));
    expect(result.current.width).toBe(420);
  });

  it("dragging the handle right grows the width, clamped to maxWidth", () => {
    const { result } = renderHook(() =>
      useResizablePanel({ initialWidth: 300, minWidth: 200, maxWidth: 500 }),
    );
    act(() => startDrag(result.current.handleProps.onMouseDown, 100));
    act(() => mouseMove(400)); // +300 from 300 = 600 → clamped to 500
    expect(result.current.width).toBe(500);
  });

  it("dragging left shrinks the width, clamped to minWidth", () => {
    const { result } = renderHook(() =>
      useResizablePanel({ initialWidth: 300, minWidth: 200, maxWidth: 500 }),
    );
    act(() => startDrag(result.current.handleProps.onMouseDown, 100));
    act(() => mouseMove(0)); // -100 from 300 = 200 (exactly min)
    expect(result.current.width).toBe(200);
  });

  it("stops resizing after mouseup (listeners cleaned up)", () => {
    const { result } = renderHook(() =>
      useResizablePanel({ initialWidth: 300, minWidth: 200, maxWidth: 500 }),
    );
    act(() => startDrag(result.current.handleProps.onMouseDown, 100));
    act(() => mouseMove(350)); // 300 + 250 = 550 → clamped 500
    expect(result.current.width).toBe(500);
    act(() => document.dispatchEvent(new MouseEvent("mouseup")));
    act(() => mouseMove(100)); // ignored now — width stays put
    expect(result.current.width).toBe(500);
  });
});
