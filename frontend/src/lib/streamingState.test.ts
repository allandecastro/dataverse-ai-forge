import { describe, it, expect } from "vitest";
import { classifyToolResultStatus, shouldKeepBubble } from "./streamingState";

describe("classifyToolResultStatus", () => {
  // ─── Quote-wrapped (the realistic shape — backend JSON-encodes results) ───

  it("detects ERROR-prefixed results wrapped in quotes", () => {
    // Symptom this protects: framework wraps `ERROR: foo` as `"\"ERROR: foo\""`,
    // so a naive startsWith check missed it and the tool was marked complete.
    expect(classifyToolResultStatus('"ERROR: 401 Unauthorized"')).toBe("error");
  });

  it("detects framework-style 'Error:' prefix wrapped in quotes", () => {
    expect(classifyToolResultStatus('"Error: something went wrong"')).toBe("error");
  });

  it("handles whitespace after Error keyword (Error<space>)", () => {
    expect(classifyToolResultStatus('"Error 500: server failure"')).toBe("error");
  });

  it("returns complete for normal success messages", () => {
    expect(classifyToolResultStatus('"Created table adc_phone (MetadataId=...)"')).toBe("complete");
  });

  // ─── Unwrapped (defensive — should still classify correctly) ───

  it("detects unwrapped ERROR prefix", () => {
    expect(classifyToolResultStatus("ERROR: 404 not found")).toBe("error");
  });

  it("returns complete for unwrapped success", () => {
    expect(classifyToolResultStatus("Success!")).toBe("complete");
  });

  // ─── Edge cases ───

  it("does not match 'error' embedded mid-string", () => {
    // The word "error" appearing in the middle of a result is not a failure
    // signal — only a leading Error[:space] is.
    expect(classifyToolResultStatus('"Created with 0 errors detected"')).toBe("complete");
  });

  it("returns complete for empty string", () => {
    expect(classifyToolResultStatus("")).toBe("complete");
  });

  it("returns complete for the literal string 'null'", () => {
    expect(classifyToolResultStatus("null")).toBe("complete");
  });

  it("matches case-insensitively (ERROR / error / Error)", () => {
    expect(classifyToolResultStatus('"error: lowercased"')).toBe("error");
    expect(classifyToolResultStatus('"ERROR: uppercase"')).toBe("error");
    expect(classifyToolResultStatus('"eRrOr: mixed"')).toBe("error");
  });

  it("does not strip quotes when only one side is quoted", () => {
    // A leading quote without a trailing quote shouldn't be unwrapped — that's
    // a malformed result, but we should classify it conservatively as success
    // unless the inside text starts with error.
    expect(classifyToolResultStatus('"unmatched')).toBe("complete");
    expect(classifyToolResultStatus('unmatched"')).toBe("complete");
  });
});

describe("shouldKeepBubble", () => {
  it("returns true for normal content", () => {
    expect(shouldKeepBubble("Hello there")).toBe(true);
  });

  it("returns false for empty string", () => {
    expect(shouldKeepBubble("")).toBe(false);
  });

  it("returns false for whitespace-only content", () => {
    expect(shouldKeepBubble("   ")).toBe(false);
    expect(shouldKeepBubble("\n\n")).toBe(false);
    expect(shouldKeepBubble("\t \r\n  ")).toBe(false);
  });

  it("returns true for content surrounded by whitespace", () => {
    expect(shouldKeepBubble("  hi  ")).toBe(true);
  });

  it("returns true for a single visible character", () => {
    expect(shouldKeepBubble(".")).toBe(true);
  });
});
