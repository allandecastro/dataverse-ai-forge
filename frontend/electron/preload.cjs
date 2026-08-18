// Electron preload — runs in an isolated context before the page scripts.
//
// Stamps the platform onto the document root so the app's CSS can adapt to the
// desktop shell. On macOS the title bar is hidden, so the renderer keys off
// :root[data-electron="darwin"] to reserve a draggable top strip for the
// traffic lights. Values: "darwin" | "win32" | "linux".
//
// Also exposes a tiny, explicit bridge (window.electronAPI) for the desktop-only
// features the browser can't do: a native folder picker and a process relaunch,
// both used by the "Data & storage" panel to relocate the data directory. The
// bridge is deliberately minimal — no general ipcRenderer access leaks to the page.

const { contextBridge, ipcRenderer } = require("electron");

const platform = process.platform;

function applyPlatform() {
  if (document.documentElement) {
    document.documentElement.dataset.electron = platform;
  }
}

if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", applyPlatform, { once: true });
} else {
  applyPlatform();
}

contextBridge.exposeInMainWorld("electronAPI", {
  isElectron: true,
  platform,
  /** Opens a native folder picker. Resolves to the chosen absolute path, or null if cancelled. */
  pickFolder: () => ipcRenderer.invoke("pick-folder"),
  /** Restarts the app so the backend applies a pending data-dir move on next boot. */
  relaunch: () => ipcRenderer.invoke("relaunch"),
});
