// Electron preload — runs in an isolated context before the page scripts.
//
// Stamps the platform onto the document root so the app's CSS can adapt to the
// desktop shell. On macOS the title bar is hidden, so the renderer keys off
// :root[data-electron="darwin"] to reserve a draggable top strip for the
// traffic lights. Values: "darwin" | "win32" | "linux".

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
