import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { useProjects } from "./useProjects";
import { STORAGE_SESSION_ID } from "@/lib/constants";

const fetchMock = vi.fn();

beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  fetchMock.mockReset();
  localStorage.clear();
});
afterEach(() => vi.unstubAllGlobals());

function okJson(body: unknown) {
  return { ok: true, json: async () => body };
}

describe("useProjects", () => {
  it("fetches sessions on mount", async () => {
    fetchMock.mockResolvedValue(okJson({ sessions: [{ sessionId: "s1", projectName: "P1" }] }));
    const { result } = renderHook(() => useProjects());
    await waitFor(() => expect(result.current.sessions).toHaveLength(1));
    expect(result.current.sessions[0].sessionId).toBe("s1");
    expect(result.current.loading).toBe(false);
  });

  it("restores selectedSessionId from localStorage on mount", async () => {
    localStorage.setItem(STORAGE_SESSION_ID, "restored");
    fetchMock.mockResolvedValue(okJson({ sessions: [] }));
    const { result } = renderHook(() => useProjects());
    await waitFor(() => expect(result.current.selectedSessionId).toBe("restored"));
  });

  it("createNewSession posts, selects, returns the id; rejects blank names", async () => {
    fetchMock.mockResolvedValue(okJson({ sessions: [] }));
    const { result } = renderHook(() => useProjects());
    await waitFor(() => expect(result.current.loading).toBe(false));

    // Blank name → null, no POST fired.
    const before = fetchMock.mock.calls.length;
    let blank: string | null = "x";
    await act(async () => {
      blank = await result.current.createNewSession("   ");
    });
    expect(blank).toBeNull();
    expect(fetchMock.mock.calls.length).toBe(before);

    // Real name → POST returns an id, hook selects it and persists it.
    fetchMock.mockResolvedValueOnce(okJson({ sessionId: "new1" })); // POST
    fetchMock.mockResolvedValueOnce(okJson({ sessions: [{ sessionId: "new1", projectName: "New" }] })); // refetch
    let id: string | null = null;
    await act(async () => {
      id = await result.current.createNewSession("New");
    });
    expect(id).toBe("new1");
    expect(result.current.selectedSessionId).toBe("new1");
    expect(localStorage.getItem(STORAGE_SESSION_ID)).toBe("new1");
  });

  it("deleteSession clears the selection when the deleted session was selected", async () => {
    localStorage.setItem(STORAGE_SESSION_ID, "s1");
    fetchMock.mockResolvedValue(okJson({ sessions: [] }));
    const { result } = renderHook(() => useProjects());
    await waitFor(() => expect(result.current.selectedSessionId).toBe("s1"));

    await act(async () => {
      await result.current.deleteSession("s1");
    });
    expect(result.current.selectedSessionId).toBeNull();
    expect(localStorage.getItem(STORAGE_SESSION_ID)).toBeNull();
  });
});
