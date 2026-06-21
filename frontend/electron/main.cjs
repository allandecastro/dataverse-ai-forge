// Electron desktop wrapper for Dataverse AI Forge.
//
// The frontend is a Next.js app with live server routes (the /api/chat SSE
// proxy), so we can't ship a static export — Electron boots the Next server as
// a child process, waits for the port, then loads it in a native window.
//
//   npm run electron        → dev server + window (hot reload)
//   npm run electron:start  → production server (run `npm run build` first)
//
// When packaged (app.isPackaged), there is no npm or source tree on the user's
// machine, so we boot the Next "standalone" output (.next/standalone/server.js,
// shipped under resources/standalone) using Electron's own binary running as a
// plain Node process (ELECTRON_RUN_AS_NODE=1).

const { app, BrowserWindow, Menu, shell, nativeImage } = require("electron");
const { spawn } = require("child_process");
const http = require("http");
const path = require("path");

// Dev shows "Electron" in the macOS menu bar / app switcher because the unpacked
// binary has no productName. Force it (must run before the menu is built at ready).
// Packaged builds already get this from electron-builder's productName.
const APP_NAME = "Dataverse AI Forge";
app.setName(APP_NAME);

const PORT = Number(process.env.PORT) || 3000;
const APP_URL = `http://localhost:${PORT}`;
// ELECTRON_MODE=start runs the built server; anything else runs `next dev`.
const NEXT_SCRIPT = process.env.ELECTRON_MODE === "start" ? "start" : "dev";

let serverProc = null;
let mainWindow = null;

function startNextServer() {
  if (app.isPackaged) {
    // Packaged build: no npm / source tree available. Run the bundled Next
    // standalone server with Electron's binary acting as Node.
    const serverEntry = path.join(process.resourcesPath, "standalone", "server.js");
    serverProc = spawn(process.execPath, [serverEntry], {
      env: {
        ...process.env,
        PORT: String(PORT),
        HOSTNAME: "127.0.0.1",
        NODE_ENV: "production",
        ELECTRON_RUN_AS_NODE: "1",
      },
      stdio: "inherit",
    });
  } else {
    // Dev / unpacked: drive the Next CLI through npm (dev or start).
    const npm = process.platform === "win32" ? "npm.cmd" : "npm";
    serverProc = spawn(npm, ["run", NEXT_SCRIPT], {
      cwd: path.join(__dirname, ".."),
      env: { ...process.env, PORT: String(PORT) },
      stdio: "inherit",
      shell: process.platform === "win32",
    });
  }
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
  const isMac = process.platform === "darwin";
  mainWindow = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 980,
    minHeight: 640,
    backgroundColor: "#0a0b0e", // matches --bg-0, no white flash on launch
    // macOS: hidden title bar with the traffic lights pinned into a ~30px top
    // strip (the renderer makes that strip draggable). Windows/Linux keep their
    // native frame so users retain min/max/close controls.
    ...(isMac
      ? {
          titleBarStyle: "hidden",
          trafficLightPosition: { x: 12, y: 8 },
        }
      : {}),
    show: false,
    webPreferences: {
      preload: path.join(__dirname, "preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false,
    },
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
  // Dev/unpacked on macOS shows the generic Electron dock icon; point it at the
  // brand mark. (The packaged build gets its icon from build-resources/icon.icns.)
  if (process.platform === "darwin" && !app.isPackaged && app.dock) {
    const iconPath = path.join(__dirname, "..", "build-resources", "icon.png");
    const img = nativeImage.createFromPath(iconPath);
    if (!img.isEmpty()) app.dock.setIcon(img);
  }

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
