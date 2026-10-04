// `npm audit --audit-level=high` with a reviewed allowlist. npm has no native way to
// accept an advisory that has no patched version, so this reads `npm audit --json`
// and fails only on high/critical advisories that are NOT listed below.
//
// Every entry needs a reason and should be removed as soon as a fix ships.
import { execSync } from "node:child_process";

const ALLOWLIST = {
  // braces <=3.0.3 — stack-exhaustion DoS on deeply nested glob patterns. No patched
  // release exists (3.0.3 is the latest). Reached only through the lint toolchain
  // (eslint-config-next → @next/eslint-plugin-next → fast-glob → micromatch), never
  // shipped in the app, and only ever fed our own repo's glob patterns.
  "GHSA-vfj7-8cjw-p6xm": "braces: no fix available, dev-only lint dependency",
};

const SEVERE = new Set(["high", "critical"]);

let raw;
try {
  raw = execSync("npm audit --json", { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
} catch (err) {
  // npm audit exits non-zero when it finds anything; the JSON is still on stdout.
  raw = err.stdout;
}
const report = JSON.parse(raw);

// Collect the advisories themselves (objects in `via`), not the packages that merely
// depend on a vulnerable package (strings in `via`).
const advisories = new Map();
for (const vuln of Object.values(report.vulnerabilities ?? {})) {
  for (const via of vuln.via) {
    if (typeof via === "object" && SEVERE.has(via.severity)) {
      const id = via.url.split("/").pop();
      advisories.set(id, { ...via, id });
    }
  }
}

const blocking = [...advisories.values()].filter((a) => !(a.id in ALLOWLIST));
const allowed = [...advisories.values()].filter((a) => a.id in ALLOWLIST);

for (const a of allowed) console.log(`allowed  ${a.severity.padEnd(8)} ${a.name} ${a.id} — ${ALLOWLIST[a.id]}`);
for (const a of blocking) console.log(`BLOCKING ${a.severity.padEnd(8)} ${a.name} ${a.range} — ${a.title} (${a.url})`);

for (const id of Object.keys(ALLOWLIST)) {
  if (!advisories.has(id)) console.log(`note: allowlisted ${id} no longer reported — remove it from the allowlist`);
}

if (blocking.length > 0) {
  console.error(`\n${blocking.length} high/critical advisor${blocking.length === 1 ? "y" : "ies"} not allowlisted.`);
  process.exit(1);
}
console.log("\nNo blocking high/critical advisories.");
