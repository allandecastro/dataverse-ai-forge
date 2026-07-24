import { describe, it, expect, vi, afterEach } from "vitest";
import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApprovalCard } from "./ApprovalCard";
import type { RiskLevel } from "@/lib/types";

// Mock the workspace BPF visualizer — its full render isn't relevant to the
// approval-card semantics we're testing, and pulling it in drags layout deps.
vi.mock("@/components/workspace/BpfVisualizer", () => ({
  BpfVisualizer: ({ stageFlowDiagram }: { stageFlowDiagram: string }) => (
    <div data-testid="bpf-vis">{stageFlowDiagram}</div>
  ),
}));

afterEach(() => cleanup());

const baseProps = {
  operationType: "create_table",
  description: "Create adc_phone",
  riskLevel: "orange" as RiskLevel,
  details: '{"tableLogicalName":"adc_phone"}',
  status: "pending" as const,
};

describe("ApprovalCard — a11y semantics", () => {
  it("renders the card as a labelled group named by the visible heading", () => {
    render(<ApprovalCard {...baseProps} agentName="DataModelBuilder" />);

    // role="group" + aria-labelledby → the accessible name is the visible
    // heading text ("Approval Required · from DataModelBuilder"). Operation
    // type + risk live in the body and get read as the SR moves through.
    const group = screen.getByRole("group", {
      name: /Approval Required.*from DataModelBuilder/i,
    });
    expect(group).toBeTruthy();
  });

  it("uses a batch-shaped heading when batchItems is provided", () => {
    render(
      <ApprovalCard
        {...baseProps}
        operationType="batch_create"
        agentName="DataModelBuilder"
        batchItems={[
          { planId: "p1", summary: "A", riskLevel: "yellow" },
          { planId: "p2", summary: "B", riskLevel: "orange" },
        ]}
      />,
    );

    // Heading is "Batch Approval — 2 operations · from DataModelBuilder";
    // matching on both halves proves the count + agent both reached the heading.
    expect(
      screen.getByRole("group", { name: /Batch Approval.*2 operations.*DataModelBuilder/i }),
    ).toBeTruthy();
  });

  it("exposes the Details toggle as a button with aria-expanded + aria-controls", async () => {
    const user = userEvent.setup();
    // Low-risk op: Details starts collapsed (high-risk auto-opens — covered below).
    render(<ApprovalCard {...baseProps} riskLevel="green" />);

    const toggle = screen.getByRole("button", { name: /^Details$/i });
    // Default: collapsed.
    expect(toggle.getAttribute("aria-expanded")).toBe("false");
    const panelId = toggle.getAttribute("aria-controls");
    expect(panelId).toBeTruthy();
    // The panel isn't in the DOM while collapsed — clicking opens it and
    // the disclosed element must carry the id `aria-controls` points to.
    await user.click(toggle);
    expect(toggle.getAttribute("aria-expanded")).toBe("true");
    expect(document.getElementById(panelId!)).toBeTruthy();
  });

  it("gives Approve / Edit / Reject explicit aria-labels that include the operation type", () => {
    render(<ApprovalCard {...baseProps} />);

    // Each action button: aria-label takes precedence so screen readers say
    // "Approve create_table" not just "Approve".
    expect(screen.getByRole("button", { name: /Approve create_table/i })).toBeTruthy();
    expect(screen.getByRole("button", { name: /Request changes to create_table/i })).toBeTruthy();
    expect(screen.getByRole("button", { name: /Reject create_table/i })).toBeTruthy();
  });

  it("renders Code Preview tabs with proper tab/tabpanel semantics + roving tabindex", async () => {
    const user = userEvent.setup();
    const details = JSON.stringify({
      csharpCode: "public class X {}",
      unitTestCode: "[Fact] void Y(){}",
    });
    render(<ApprovalCard {...baseProps} details={details} />);

    await user.click(screen.getByRole("button", { name: /Code Preview/i }));

    const tablist = screen.getByRole("tablist", { name: /Code views/i });
    const tabs = within(tablist).getAllByRole("tab");
    expect(tabs).toHaveLength(2);

    // First tab selected by default.
    expect(tabs[0].getAttribute("aria-selected")).toBe("true");
    expect(tabs[0].getAttribute("tabindex")).toBe("0");
    expect(tabs[1].getAttribute("aria-selected")).toBe("false");
    expect(tabs[1].getAttribute("tabindex")).toBe("-1");

    // Click the second tab — selection + tabindex swap.
    await user.click(tabs[1]);
    expect(tabs[0].getAttribute("aria-selected")).toBe("false");
    expect(tabs[0].getAttribute("tabindex")).toBe("-1");
    expect(tabs[1].getAttribute("aria-selected")).toBe("true");
    expect(tabs[1].getAttribute("tabindex")).toBe("0");

    // The visible panel is labelled by the active tab (aria-labelledby pair).
    const panel = screen.getByRole("tabpanel");
    expect(panel.getAttribute("aria-labelledby")).toBe(tabs[1].id);
  });
});

describe("ApprovalCard — behavior", () => {
  it("fires the right callback for each action button", async () => {
    const user = userEvent.setup();
    const onApprove = vi.fn();
    const onEdit = vi.fn();
    const onReject = vi.fn();
    render(
      <ApprovalCard
        {...baseProps}
        onApprove={onApprove}
        onEdit={onEdit}
        onReject={onReject}
      />,
    );

    await user.click(screen.getByRole("button", { name: /Approve create_table/i }));
    await user.click(screen.getByRole("button", { name: /Request changes to create_table/i }));
    await user.click(screen.getByRole("button", { name: /Reject create_table/i }));

    expect(onApprove).toHaveBeenCalledTimes(1);
    expect(onEdit).toHaveBeenCalledTimes(1);
    expect(onReject).toHaveBeenCalledTimes(1);
  });

  it("requires two clicks to approve a Critical (red) operation", async () => {
    const user = userEvent.setup();
    const onApprove = vi.fn();
    render(<ApprovalCard {...baseProps} riskLevel="red" onApprove={onApprove} />);

    // First click arms the confirmation — onApprove must NOT fire yet.
    await user.click(screen.getByRole("button", { name: /Approve create_table/i }));
    expect(onApprove).not.toHaveBeenCalled();

    // Button now asks for explicit confirmation; the second click applies it.
    await user.click(screen.getByRole("button", { name: /Confirm create_table — irreversible/i }));
    expect(onApprove).toHaveBeenCalledTimes(1);
  });

  it("opens Details by default for a high-risk operation", () => {
    // orange (High) → the user sees what changes without expanding first.
    render(<ApprovalCard {...baseProps} riskLevel="orange" />);
    const toggle = screen.getByRole("button", { name: /^Details$/i });
    expect(toggle.getAttribute("aria-expanded")).toBe("true");
  });

  it("renders batch items as an ordered list when batchItems is provided", () => {
    render(
      <ApprovalCard
        {...baseProps}
        operationType="batch_create"
        description="Create 2 fields"
        batchItems={[
          { planId: "p1", summary: "Create field A", riskLevel: "yellow" },
          { planId: "p2", summary: "Create field B", riskLevel: "orange" },
        ]}
      />,
    );

    // Heading switches to batch wording (visible to sighted users).
    expect(screen.getByText(/Batch Approval — 2 operations/i)).toBeTruthy();
    // Each summary appears in the list.
    expect(screen.getByText(/Create field A/)).toBeTruthy();
    expect(screen.getByText(/Create field B/)).toBeTruthy();
  });

  it("hides action buttons and shows a status pill once the decision is recorded", () => {
    render(<ApprovalCard {...baseProps} status="approved" />);

    expect(screen.queryByRole("button", { name: /Approve/i })).toBeNull();
    expect(screen.getByText(/^Approved$/)).toBeTruthy();
  });
});
