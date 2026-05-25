import { describe, it, expect, vi, afterEach } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ConfirmDialog } from "./ConfirmDialog";

afterEach(() => cleanup());

describe("ConfirmDialog — a11y semantics", () => {
  it("renders nothing when open is false", () => {
    render(
      <ConfirmDialog
        open={false}
        title="Discard changes?"
        message="Anything"
        onConfirm={() => {}}
        onCancel={() => {}}
      />,
    );
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("exposes role=dialog + aria-modal + label/description references", () => {
    render(
      <ConfirmDialog
        open
        title="Discard changes?"
        message="Your unsaved edits will be lost."
        onConfirm={() => {}}
        onCancel={() => {}}
      />,
    );

    const dialog = screen.getByRole("dialog");
    expect(dialog.getAttribute("aria-modal")).toBe("true");

    // aria-labelledby and aria-describedby must point at real elements that
    // carry the title / message text — otherwise screen readers say nothing.
    const titleId = dialog.getAttribute("aria-labelledby");
    const descId = dialog.getAttribute("aria-describedby");
    expect(titleId).toBeTruthy();
    expect(descId).toBeTruthy();
    expect(document.getElementById(titleId!)?.textContent).toBe("Discard changes?");
    expect(document.getElementById(descId!)?.textContent).toBe(
      "Your unsaved edits will be lost.",
    );
  });
});

describe("ConfirmDialog — focus management", () => {
  it("moves focus to the Cancel button when opened", async () => {
    render(
      <ConfirmDialog
        open
        title="Confirm"
        message="Sure?"
        onConfirm={() => {}}
        onCancel={() => {}}
      />,
    );

    // Cancel is the safer default focus target for destructive ops.
    const cancel = screen.getByRole("button", { name: "Cancel" });
    expect(document.activeElement).toBe(cancel);
  });

  it("restores focus to the previously-focused element on close", async () => {
    // Render a "trigger" button outside the dialog, focus it, then open the
    // dialog — when the dialog unmounts, focus must return to the trigger.
    function Harness({ open }: { open: boolean }) {
      return (
        <>
          <button type="button" data-testid="trigger">Open</button>
          <ConfirmDialog
            open={open}
            title="t"
            message="m"
            onConfirm={() => {}}
            onCancel={() => {}}
          />
        </>
      );
    }
    const { rerender } = render(<Harness open={false} />);
    const trigger = screen.getByTestId("trigger");
    trigger.focus();
    expect(document.activeElement).toBe(trigger);

    rerender(<Harness open />);
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Cancel" }));

    rerender(<Harness open={false} />);
    expect(document.activeElement).toBe(trigger);
  });
});

describe("ConfirmDialog — interaction", () => {
  it("fires onCancel when Escape is pressed", async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    render(
      <ConfirmDialog
        open
        title="t"
        message="m"
        onConfirm={() => {}}
        onCancel={onCancel}
      />,
    );

    await user.keyboard("{Escape}");
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("fires the right callback for Cancel / Confirm clicks", async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    const onConfirm = vi.fn();
    render(
      <ConfirmDialog
        open
        title="t"
        message="m"
        confirmLabel="Delete"
        cancelLabel="Keep"
        destructive
        onConfirm={onConfirm}
        onCancel={onCancel}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Delete" }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onCancel).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Keep" }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("does not rebind the Escape listener when onCancel identity changes between renders", async () => {
    // Regression guard: the effect depends only on `open`, not `onCancel` —
    // a ref forwards the latest callback. Without that, inline arrow callbacks
    // from the parent would re-bind the window listener on every render.
    const user = userEvent.setup();
    const calls: number[] = [];
    function Harness() {
      // New onCancel identity on every render.
      return (
        <ConfirmDialog
          open
          title="t"
          message="m"
          onConfirm={() => {}}
          onCancel={() => calls.push(Date.now())}
        />
      );
    }
    const { rerender } = render(<Harness />);
    rerender(<Harness />);
    rerender(<Harness />);
    await user.keyboard("{Escape}");
    // Single press → single call, no matter how many times we re-rendered.
    expect(calls).toHaveLength(1);
  });
});
