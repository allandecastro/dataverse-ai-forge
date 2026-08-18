import { describe, it, expect, vi, afterEach } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EmptyChat, EXAMPLE_PROMPTS } from "./EmptyChat";

afterEach(() => cleanup());

describe("EmptyChat", () => {
  it("shows the onboarding panel (not prompts) when a connection is missing", () => {
    render(
      <EmptyChat
        onPromptClick={() => {}}
        healthKnown
        azureReady={false}
        dataverseReady={false}
        onOpenSettings={() => {}}
      />,
    );
    expect(screen.getByText("Welcome to Dataverse AI Forge")).toBeTruthy();
    expect(screen.getByRole("button", { name: /open settings/i })).toBeTruthy();
    // Example prompts must NOT render — they'd fail on the missing connection.
    expect(screen.queryByText(EXAMPLE_PROMPTS[0])).toBeNull();
  });

  it("marks a ready service as Connected in the checklist", () => {
    render(
      <EmptyChat
        onPromptClick={() => {}}
        healthKnown
        azureReady
        dataverseReady={false}
        onOpenSettings={() => {}}
      />,
    );
    // Azure is connected, Dataverse isn't → still onboarding, but shows one "Connected".
    expect(screen.getByText("Connected")).toBeTruthy();
  });

  it("clicking Open Settings calls onOpenSettings", async () => {
    const onOpenSettings = vi.fn();
    render(
      <EmptyChat
        onPromptClick={() => {}}
        healthKnown
        azureReady={false}
        dataverseReady={false}
        onOpenSettings={onOpenSettings}
      />,
    );
    await userEvent.click(screen.getByRole("button", { name: /open settings/i }));
    expect(onOpenSettings).toHaveBeenCalledOnce();
  });

  it("shows example prompts when both services are connected, and clicking one fires onPromptClick", async () => {
    const onPromptClick = vi.fn();
    render(
      <EmptyChat onPromptClick={onPromptClick} healthKnown azureReady dataverseReady />,
    );
    // No onboarding panel now.
    expect(screen.queryByText("Welcome to Dataverse AI Forge")).toBeNull();
    const first = screen.getByText(EXAMPLE_PROMPTS[0]);
    await userEvent.click(first);
    expect(onPromptClick).toHaveBeenCalledWith(EXAMPLE_PROMPTS[0]);
  });
});
