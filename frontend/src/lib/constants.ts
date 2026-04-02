// ── Layout ──
export const SIDEBAR_WIDTH_COLLAPSED = 52;
export const SIDEBAR_WIDTH_EXPANDED = 220;
export const DIAG_PANEL_WIDTH = 420;
export const MIDDLE_PANEL_MIN_WIDTH = 200;
export const MIDDLE_PANEL_DEFAULT_WIDTH = 420;
export const TEXTAREA_MAX_HEIGHT = 160;

// ── API ──
export const BACKEND_URL = process.env.NEXT_PUBLIC_BACKEND_URL || "http://localhost:5000";

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
