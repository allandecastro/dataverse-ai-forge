// Electron desktop wrapper for Dataverse AI Forge.
//
// The frontend is a Next.js app with live server routes (the /api/chat SSE
// proxy), so we can't ship a static export — Electron boots the Next server as
// a child process, waits for the port, then loads it in a native window.
//
//   npm run electron        → dev server + window (hot reload)
//   npm run electron:start  → production server (run `npm run build` first)

const { app, BrowserWindow, shell } = require("electron");
const { spawn } = require("child_process");
const http = require("http");
const path = require("path");

const PORT = Number(process.env.PORT) || 3000;
const APP_URL = `http://localhost:${PORT}`;
// ELECTRON_MODE=start runs the built server; anything else runs `next dev`.
const NEXT_SCRIPT = process.env.ELECTRON_MODE === "start" ? "start" : "dev";

let serverProc = null;
let mainWindow = null;

function startNextServer() {
  const npm = process.platform === "win32" ? "npm.cmd" : "npm";
  serverProc = spawn(npm, ["run", NEXT_SCRIPT], {
    cwd: path.join(__dirname, ".."),
    env: { ...process.env, PORT: String(PORT) },
    stdio: "inherit",
    shell: process.platform === "win32",
  });
  serverProc.on("exit", (code) => {
    if (code && code !== 0 && !app.isQuitting) {
      console.error(`Next server exited with code ${code}`);
    }
  });
}

function waitForServer(url, onReady, attempt = 0) {
  http
    .get(url, (res) => {
      res.resume();
      onReady();
    })
    .on("error", () => {
      if (attempt > 150) {
        console.error("Next server did not come up in time.");
        return;
      }
      setTimeout(() => waitForServer(url, onReady, attempt + 1), 400);
    });
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 980,
    minHeight: 640,
    backgroundColor: "#0a0b0e", // matches --bg-0, no white flash on launch
    // Standard macOS title bar so the traffic-light buttons sit in their own
    // strip instead of floating over the sidebar's top-left content.
    titleBarStyle: "default",
    show: false,
    webPreferences: { contextIsolation: true, nodeIntegration: false },
  });

  mainWindow.once("ready-to-show", () => mainWindow.show());
  mainWindow.loadURL(APP_URL);

  // External links (export downloads, docs) open in the real browser, not the app shell.
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith("http")) shell.openExternal(url);
    return { action: "deny" };
  });

  mainWindow.on("closed", () => {
    mainWindow = null;
  });
}

app.whenReady().then(() => {
  startNextServer();
  waitForServer(APP_URL, createWindow);

  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});

app.on("before-quit", () => {
  app.isQuitting = true;
  if (serverProc) serverProc.kill();
});
