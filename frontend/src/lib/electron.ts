// Typed accessor for the desktop-only bridge exposed by electron/preload.cjs.
// In the browser (dev, or the web deployment) window.electronAPI is undefined,
// so callers fall back to a manual path input.

export interface ElectronAPI {
  isElectron: boolean;
  platform: string;
  /** Native folder picker. Resolves to the chosen absolute path, or null if cancelled. */
  pickFolder: () => Promise<string | null>;
  /** Restarts the desktop app (so a pending data-dir move is applied on next boot). */
  relaunch: () => Promise<void>;
}

declare global {
  interface Window {
    electronAPI?: ElectronAPI;
  }
}

export function getElectronAPI(): ElectronAPI | null {
  if (typeof window === "undefined") return null;
  return window.electronAPI ?? null;
}
