// ── Layout ──
export const SIDEBAR_WIDTH_COLLAPSED = 52;
export const SIDEBAR_WIDTH_EXPANDED = 220;
export const DIAG_PANEL_WIDTH = 420;
export const MIDDLE_PANEL_MIN_WIDTH = 200;
export const MIDDLE_PANEL_DEFAULT_WIDTH = 420;
export const TEXTAREA_MAX_HEIGHT = 160;

// ── API ──
// All browser-side hooks fetch `${BACKEND_URL}/api/...`. Default points at
// the Next.js catch-all proxy (`app/api/proxy/[...path]/route.ts`), which
// forwards the request to the real backend with a server-side bearer token
// attached when FORGE_API_TOKEN is set. Set NEXT_PUBLIC_BACKEND_URL to a
// real host (e.g. `http://localhost:5000`) only if you want to skip the
// proxy and hit the backend directly — that path can't carry the token, so
// it only works when the backend's Auth:SharedSecret is unset.
export const BACKEND_URL = process.env.NEXT_PUBLIC_BACKEND_URL || "/api/proxy";

// ── Agents ──
export const DEFAULT_AGENT_NAME = "SolutionArchitect";

// ── Timeouts ──
export const COPY_FEEDBACK_MS = 1500;
export const MESSAGES_PERSIST_LIMIT = 200;

// ── localStorage keys ──
export const STORAGE_SIDEBAR_COLLAPSED = "sidebar-collapsed";
export const STORAGE_CHAT_MESSAGES = (sessionId: string) => `forge-messages-${sessionId}`;
export const STORAGE_THEME = "theme";
export const STORAGE_SESSION_ID = "dataverse-forge-session-id";
export const STORAGE_BOTTOM_PANEL_HEIGHT = "bottom-panel-height";
