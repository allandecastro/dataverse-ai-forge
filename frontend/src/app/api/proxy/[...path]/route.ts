// Catch-all proxy from browser → Next.js → backend. Every request the
// browser sends as `/api/proxy/<subpath>` is forwarded to
// `<BACKEND_URL>/<subpath>` with the FORGE_API_TOKEN bearer header attached
// server-side. This is the missing piece that lets `Auth:SharedSecret` get
// turned on without 401'ing browser-direct endpoints (sessions, settings,
// audit, /healthz) — the secret never leaves the Next.js server process.
//
// The chat SSE endpoint at /api/chat keeps its own dedicated route
// (route.ts in ../chat/) — that one has a custom handshake-timeout +
// streaming wiring this generic proxy doesn't reproduce. /api/proxy/* and
// /api/chat are independent paths, no conflict.

import type { NextRequest } from "next/server";

const BACKEND_URL = process.env.BACKEND_URL || "http://localhost:5000";
const FORGE_API_TOKEN = process.env.FORGE_API_TOKEN;

// Hop-by-hop headers we don't forward (RFC 7230 §6.1 plus Next/undici-set
// ones). Re-sending them tends to confuse intermediate runtimes.
const HOP_BY_HOP = new Set([
  "connection",
  "keep-alive",
  "proxy-authenticate",
  "proxy-authorization",
  "te",
  "trailer",
  "transfer-encoding",
  "upgrade",
  "host", // Next sets the correct Host for the upstream fetch
  "content-length", // recomputed by undici from the actual body
]);

async function proxy(req: NextRequest, ctx: { params: Promise<{ path: string[] }> }) {
  const { path } = await ctx.params;
  // path = ["api", "sessions"] for /api/proxy/api/sessions → upstream /api/sessions
  const subpath = path.join("/");
  const url = new URL(`/${subpath}`, BACKEND_URL);
  // Preserve query string. `req.nextUrl.searchParams` already excludes the
  // `[...path]` route segments.
  for (const [k, v] of req.nextUrl.searchParams) {
    url.searchParams.append(k, v);
  }

  // Forward client headers minus the hop-by-hop set, then set Authorization.
  const headers = new Headers();
  req.headers.forEach((value, key) => {
    if (!HOP_BY_HOP.has(key.toLowerCase())) {
      headers.set(key, value);
    }
  });
  // The browser's own Authorization (if any) is overridden — the proxy's
  // job is to be the authorization gateway.
  if (FORGE_API_TOKEN) {
    headers.set("Authorization", `Bearer ${FORGE_API_TOKEN}`);
  } else {
    headers.delete("Authorization");
  }

  // Forward the body for methods that have one. arrayBuffer() keeps binary
  // uploads intact (the existing endpoints are JSON today, but the proxy
  // shouldn't be the thing that breaks them when a file upload lands).
  let body: BodyInit | undefined;
  if (req.method !== "GET" && req.method !== "HEAD") {
    body = await req.arrayBuffer();
  }

  let upstream: Response;
  try {
    upstream = await fetch(url, {
      method: req.method,
      headers,
      body,
      redirect: "manual", // let the caller see redirects rather than auto-following
    });
  } catch (err) {
    // Network-level failure to reach the backend (process down, DNS, etc.).
    // Mirror what /api/chat does — surface as 504 with a short JSON body.
    return new Response(
      JSON.stringify({ error: "Could not reach backend", detail: String(err) }),
      { status: 504, headers: { "Content-Type": "application/json" } },
    );
  }

  // Pass through response headers minus the hop-by-hop set.
  const resHeaders = new Headers();
  upstream.headers.forEach((value, key) => {
    if (!HOP_BY_HOP.has(key.toLowerCase())) {
      resHeaders.set(key, value);
    }
  });

  // `upstream.body` is a ReadableStream — handing it back via new Response
  // preserves streaming for any backend endpoint that wants to stream.
  return new Response(upstream.body, {
    status: upstream.status,
    statusText: upstream.statusText,
    headers: resHeaders,
  });
}

export const GET = proxy;
export const POST = proxy;
export const PATCH = proxy;
export const PUT = proxy;
export const DELETE = proxy;
export const OPTIONS = proxy;
