import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useForgeApprovals } from "./useForgeApprovals";
import type { RiskLevel } from "@/lib/types";

// Mock the chat-context module: the hook reads pendingApproval +
// clearApproval + sendMessage from it. Mocking lets us drive the hook
// without standing up the full ForgeChatProvider (SSE + reducer).
const mockClearApproval = vi.fn();
const mockSendMessage = vi.fn();
let mockPendingApproval: {
  operationType: string;
  description: string;
  riskLevel: string;
  details: string;
  batchItems?: Array<{ planId: string; summary: string; riskLevel: string }>;
  agentName?: string;
} | null = null;

vi.mock("@/hooks/useForgeChat", () => ({
  useForgeChatContext: () => ({
    pendingApproval: mockPendingApproval,
    clearApproval: mockClearApproval,
    sendMessage: mockSendMessage,
  }),
}));

// Mock the BPF visualizer pulled in transitively by ApprovalCard — same
// rationale as in ApprovalCard.test.tsx.
vi.mock("@/components/workspace/BpfVisualizer", () => ({
  BpfVisualizer: () => null,
}));

beforeEach(() => {
  mockClearApproval.mockReset();
  mockSendMessage.mockReset();
  mockPendingApproval = null;
});

afterEach(() => cleanup());

// Thin wrapper component so we can drive the hook through the React tree.
function Harness() {
  const { approvalElement } = useForgeApprovals();
  return <div data-testid="harness">{approvalElement}</div>;
}

describe("useForgeApprovals", () => {
  it("returns null element when no approval is pending", () => {
    mockPendingApproval = null;
    render(<Harness />);
    // Harness wrapper exists but is empty.
    expect(screen.getByTestId("harness").childNodes).toHaveLength(0);
  });

  it("renders an ApprovalCard from pendingApproval and passes agentName through", () => {
    mockPendingApproval = {
      operationType: "create_table",
      description: "Create adc_phone",
      riskLevel: "orange" as RiskLevel,
      details: "{}",
      agentName: "DataModelBuilder",
    };
    render(<Harness />);

    // The card's group label (from aria-labelledby) is the visible heading,
    // which includes "· from DataModelBuilder" — proves agentName flowed
    // through. operationType ("create_table") then flows separately into
    // the action-button aria-labels we check elsewhere.
    expect(
      screen.getByRole("group", { name: /Approval Required.*from DataModelBuilder/i }),
    ).toBeTruthy();
    expect(
      screen.getByRole("button", { name: /Approve create_table/i }),
    ).toBeTruthy();
  });

  it("on Approve: clears approval and sends a neutral receipt message", async () => {
    const user = userEvent.setup();
    mockPendingApproval = {
      operationType: "create_table",
      description: "x",
      riskLevel: "orange",
      details: "{}",
    };
    render(<Harness />);

    await user.click(screen.getByRole("button", { name: /Approve create_table/i }));

    expect(mockClearApproval).toHaveBeenCalledTimes(1);
    expect(mockSendMessage).toHaveBeenCalledTimes(1);
    expect(mockSendMessage).toHaveBeenCalledWith(
      "User approved.",
      { kind: "approval-receipt" },
    );
  });

  it("on Edit: clears approval and sends edit intent", async () => {
    const user = userEvent.setup();
    mockPendingApproval = {
      operationType: "create_table",
      description: "x",
      riskLevel: "orange",
      details: "{}",
    };
    render(<Harness />);

    await user.click(screen.getByRole("button", { name: /Edit create_table before approving/i }));

    expect(mockClearApproval).toHaveBeenCalledTimes(1);
    expect(mockSendMessage).toHaveBeenCalledWith(
      "User wants to edit.",
      { kind: "approval-edit" },
    );
  });

  it("on Reject: clears approval and sends rejection", async () => {
    const user = userEvent.setup();
    mockPendingApproval = {
      operationType: "create_table",
      description: "x",
      riskLevel: "orange",
      details: "{}",
    };
    render(<Harness />);

    await user.click(screen.getByRole("button", { name: /Reject create_table/i }));

    expect(mockClearApproval).toHaveBeenCalledTimes(1);
    expect(mockSendMessage).toHaveBeenCalledWith(
      "User rejected.",
      { kind: "approval-rejection" },
    );
  });

  it("messages are neutral, contain no GUIDs / planIds / counts (content-filter regression)", async () => {
    // Regression guard for the comment in useForgeApprovals: AOAI's content
    // filter has flagged variants like "User approved all 4 operations.
    // Execute them." Keep the literal strings boring and short.
    const user = userEvent.setup();
    mockPendingApproval = {
      operationType: "batch_create",
      description: "Create 2 fields",
      riskLevel: "orange",
      details: "{}",
      batchItems: [
        { planId: "p1", summary: "A", riskLevel: "yellow" },
        { planId: "p2", summary: "B", riskLevel: "orange" },
      ],
    };
    render(<Harness />);

    await user.click(screen.getByRole("button", { name: /Approve batch_create/i }));

    const sent = mockSendMessage.mock.calls[0][0] as string;
    expect(sent).not.toMatch(/p1|p2/);
    expect(sent).not.toMatch(/\d/); // no counts
    expect(sent.length).toBeLessThan(40);
  });
});
