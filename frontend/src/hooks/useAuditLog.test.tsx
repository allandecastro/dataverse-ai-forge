import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useAuditLog } from "./useAuditLog";

const fetchMock = vi.fn();

beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  fetchMock.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

function okJson(body: unknown) {
  return { ok: true, json: async () => body };
}

describe("useAuditLog", () => {
  it("returns empty and does not fetch when sessionId is null", async () => {
    const { result } = renderHook(() => useAuditLog(null));
    // let the deferred effect run
    await Promise.resolve();
    await Promise.resolve();
    expect(result.current.auditEntries).toEqual([]);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("fetches and maps audit entries to diagnostic entries", async () => {
    fetchMock.mockResolvedValue(
      okJson({
        entries: [
          { entryId: "e1", timestamp: "2026-08-18T10:00:00Z", agentName: "DataModelBuilder", action: "PlanCreateTable", detail: "ok", success: true },
          { entryId: "e2", timestamp: "2026-08-18T10:01:00Z", agentName: "Tester", action: "Verify", detail: "boom", success: false },
        ],
      }),
    );
    const { result } = renderHook(() => useAuditLog("s1"));
    await waitFor(() => expect(result.current.auditEntries).toHaveLength(2));

    const e1 = result.current.auditEntries[0];
    expect(e1.id).toBe("audit-e1"); // prefixed so it can't collide with tool-call ids
    expect(e1.agent).toBe("DataModelBuilder");
    expect(e1.toolName).toBe("PlanCreateTable");
    expect(e1.result).toBe("ok");
    expect(e1.duration).toBeNull();
    expect(e1.status).toBe("complete");
    expect(result.current.auditEntries[1].status).toBe("error"); // success:false → error

    // Hit the right endpoint.
    expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining("/api/projects/s1/audit"));
  });

  it("clears entries when sessionId goes null", async () => {
    fetchMock.mockResolvedValue(
      okJson({
        entries: [
          { entryId: "e1", timestamp: "2026-08-18T10:00:00Z", agentName: "A", action: "X", detail: "d", success: true },
        ],
      }),
    );
    const { result, rerender } = renderHook(({ sid }: { sid: string | null }) => useAuditLog(sid), {
      initialProps: { sid: "s1" as string | null },
    });
    await waitFor(() => expect(result.current.auditEntries).toHaveLength(1));

    rerender({ sid: null });
    await waitFor(() => expect(result.current.auditEntries).toEqual([]));
  });
});
