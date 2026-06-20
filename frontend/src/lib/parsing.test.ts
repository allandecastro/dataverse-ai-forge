import { describe, it, expect } from "vitest";
import { safeParse, formatJson } from "./parsing";

describe("safeParse", () => {
  it("parses valid JSON objects and arrays", () => {
    expect(safeParse('{"a":1}')).toEqual({ a: 1 });
    expect(safeParse("[1,2,3]")).toEqual([1, 2, 3]);
  });

  it("returns the original string when input is not valid JSON", () => {
    expect(safeParse("not json")).toBe("not json");
    expect(safeParse("")).toBe("");
    expect(safeParse("Dataverse error: 400 Bad Request")).toBe("Dataverse error: 400 Bad Request");
  });
});

describe("formatJson", () => {
  it("pretty-prints valid JSON with 2-space indent", () => {
    expect(formatJson('{"a":1}')).toBe('{\n  "a": 1\n}');
  });

  it("returns the raw string for non-JSON input", () => {
    expect(formatJson("plain text")).toBe("plain text");
  });

  it("re-stringifies a bare JSON string (preserving original quoting behaviour)", () => {
    expect(formatJson('"hi"')).toBe('"hi"');
  });
});
