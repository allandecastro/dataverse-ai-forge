const BACKEND_URL = process.env.BACKEND_URL || "http://localhost:5000";

// Optional shared secret. When the backend has Auth:SharedSecret set, this
// route forwards it via the Authorization header. Server-side only — never
// expose this as NEXT_PUBLIC_* (the browser would see it in the bundle).
// Other backend calls (sessions, settings, audit) still go direct from the
// browser today and will 401 if the backend gate is on. See TODO.md.
const FORGE_API_TOKEN = process.env.FORGE_API_TOKEN;

// Bound ONLY the backend handshake — i.e. how long we wait for the response
// headers to arrive. Once the body starts streaming we want it to run as long
// as the backend keeps producing tokens (large batches of tool calls — table
// creates, multi-field creates, publish, export — can run 5+ minutes end to end).
//
// Past failure: this used `AbortSignal.timeout(HANDSHAKE_TIMEOUT_MS)` directly,
// which aborts the WHOLE fetch (including the body) at the deadline. After
// 60s of in-flight tool execution the abort fired mid-stream and the user saw
// "Connection error: network error" with the agent's run partially complete.
// The fix: manual AbortController + clearTimeout once response headers arrive,
// so the timeout bounds only the headers/handshake.
const HANDSHAKE_TIMEOUT_MS = 60_000;

export async function POST(req: Request) {
  const body = await req.text();

  // Basic structural validation of the request
  try {
    const parsed = JSON.parse(body);
    if (!parsed || typeof parsed !== "object") {
      return new Response(JSON.stringify({ error: "Invalid request body" }), {
        status: 400,
        headers: { "Content-Type": "application/json" },
      });
    }
  } catch {
    return new Response(JSON.stringify({ error: "Malformed JSON" }), {
      status: 400,
      headers: { "Content-Type": "application/json" },
    });
  }

  const controller = new AbortController();
  const handshakeTimer = setTimeout(() => controller.abort(), HANDSHAKE_TIMEOUT_MS);

  let res: Response;
  try {
    const headers: Record<string, string> = { "Content-Type": "application/json" };
    if (FORGE_API_TOKEN) {
      headers.Authorization = `Bearer ${FORGE_API_TOKEN}`;
    }
    res = await fetch(`${BACKEND_URL}/api/agui`, {
      method: "POST",
      headers,
      body,
      signal: controller.signal,
    });
    // Headers received — release the handshake bound. The streaming body now
    // owns its own lifecycle (closes when the backend ends the SSE stream).
    clearTimeout(handshakeTimer);
  } catch (err) {
    clearTimeout(handshakeTimer);
    const isAbort = err instanceof DOMException && err.name === "AbortError";
    return new Response(
      JSON.stringify({
        error: isAbort
          ? `Backend did not respond within ${HANDSHAKE_TIMEOUT_MS / 1000}s`
          : "Could not reach backend",
      }),
      {
        status: 504,
        headers: { "Content-Type": "application/json" },
      },
    );
  }

  if (!res.ok) {
    return new Response(JSON.stringify({ error: `Backend returned ${res.status}` }), {
      status: res.status,
      headers: { "Content-Type": "application/json" },
    });
  }

  return new Response(res.body, {
    headers: {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache",
      Connection: "keep-alive",
    },
  });
}
