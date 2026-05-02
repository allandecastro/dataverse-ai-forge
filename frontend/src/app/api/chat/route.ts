const BACKEND_URL = process.env.BACKEND_URL || "http://localhost:5000";

// Bound the backend handshake. After the response starts streaming, the abort
// signal is no longer relevant (the stream itself ends when the backend closes).
// Keeps the route from hanging forever if the backend is wedged before producing
// the first byte.
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

  let res: Response;
  try {
    res = await fetch(`${BACKEND_URL}/api/agui`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body,
      signal: AbortSignal.timeout(HANDSHAKE_TIMEOUT_MS),
    });
  } catch (err) {
    const isTimeout = err instanceof DOMException && err.name === "TimeoutError";
    return new Response(
      JSON.stringify({
        error: isTimeout
          ? "Backend did not respond within 60s"
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
