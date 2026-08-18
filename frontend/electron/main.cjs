// Electron desktop wrapper for Dataverse AI Forge.
//
// The desktop app owns BOTH server processes so the end user never has to
// launch anything by hand:
//   1. the .NET backend (:5100) — agents, Dataverse, data-dir move-on-startup;
//   2. the Next.js frontend (:3000) — UI + the /api/proxy + /api/chat SSE routes,
//      pointed at the backend via BACKEND_URL.
// Electron boots the backend, waits for it, boots Next pointed at it, then opens
// the window. "Restart now" (relaunch) and quit tear down both children, so the
// data-dir move — which the backend applies at startup — actually takes effect
// when the user clicks Restart.
//
//   npm run electron        → dev servers + window (Next hot reload)
//   npm run electron:start  → built Next server (run `npm run build` first)
//
// When packaged (app.isPackaged): the Next standalone server ships under
// resources/standalone; the published .NET backend ships under resources/backend
// (bundling that binary per-platform is the follow-up to this wiring).

const { app, BrowserWindow, Menu, shell, nativeImage, ipcMain, dialog } = require("electron");
const { autoUpdater } = require("electron-updater");
const { spawn } = require("child_process");
const http = require("http");
const path = require("path");
const fs = require("fs");

// Dev shows "Electron" in the macOS menu bar / app switcher because the unpacked
// binary has no productName. Force it (must run before the menu is built at ready).
// Packaged builds already get this from electron-builder's productName.
const APP_NAME = "Dataverse AI Forge";
app.setName(APP_NAME);

const PORT = Number(process.env.PORT) || 3000;
const APP_URL = `http://localhost:${PORT}`;
// ELECTRON_MODE=start runs the built server; anything else runs `next dev`.
const NEXT_SCRIPT = process.env.ELECTRON_MODE === "start" ? "start" : "dev";

const BACKEND_PORT = Number(process.env.BACKEND_PORT) || 5100;
const BACKEND_URL = `http://localhost:${BACKEND_PORT}`;

let backendProc = null;
let serverProc = null;
let mainWindow = null;

// ─── .NET backend ───

function startBackend() {
  if (app.isPackaged) {
    // Packaged build: run the published, self-contained backend shipped under
    // resources/backend. (Bundling that binary per-platform is the follow-up.)
    const exe = process.platform === "win32" ? "DataverseForge.Api.exe" : "DataverseForge.Api";
    const backendExe = path.join(process.resourcesPath, "backend", exe);
    backendProc = spawn(backendExe, ["--urls", BACKEND_URL], {
      // No DVFORGE_DATA_DIR: let ForgePaths resolve the data dir from the in-app
      // pointer (OS config dir), so "Change location" is authoritative.
      env: { ...process.env, ASPNETCORE_URLS: BACKEND_URL },
      stdio: "inherit",
    });
  } else {
    // Dev / unpacked: run the backend from source.
    const env = { ...process.env };
    // Never inherit a shell DVFORGE_DATA_DIR into the desktop backend — it would
    // pin the location and disable the in-app "Change location" flow.
    delete env.DVFORGE_DATA_DIR;
    const backendDir = path.join(__dirname, "..", "..", "backend");
    const binName = process.platform === "win32" ? "DataverseForge.Api.exe" : "DataverseForge.Api";
    const builtBin = path.join(backendDir, "src", "DataverseForge.Api", "bin", "Debug", "net10.0", binName);

    if (fs.existsSync(builtBin)) {
      // Prefer the already-built apphost: backendProc IS the API process, so
      // kill() on relaunch/quit reliably frees :5100. `dotnet run` spawns the
      // app as a grandchild that can orphan and keep the port — which would
      // break the very restart the data-dir move depends on.
      backendProc = spawn(builtBin, ["--urls", BACKEND_URL], {
        cwd: backendDir,
        env,
        stdio: "inherit",
      });
    } else {
      // Not built yet — fall back to `dotnet run` (builds first). Run
      // `dotnet build DataverseForge.slnx` once for the clean-shutdown path.
      backendProc = spawn(
        "dotnet",
        ["run", "--project", "src/DataverseForge.Api", "--urls", BACKEND_URL],
        {
          cwd: backendDir,
          env,
          stdio: "inherit",
          shell: process.platform === "win32",
        },
      );
    }
  }
  backendProc.on("exit", (code) => {
    if (code && code !== 0 && !app.isQuitting) {
      console.error(`Backend exited with code ${code}`);
    }
  });
}

// Reuse a backend that's already listening (common in dev with a separate
// terminal) instead of spawning a second one that can't bind the port.
function ensureBackend(onReady) {
  http
    .get(`${BACKEND_URL}/healthz`, (res) => {
      res.resume();
      console.log(`Reusing backend already listening on ${BACKEND_URL}`);
      onReady();
    })
    .on("error", () => {
      startBackend();
      waitForServer(`${BACKEND_URL}/healthz`, onReady);
    });
}

// ─── Next.js frontend ───

function startNextServer() {
  // The frontend's proxy + chat routes read BACKEND_URL to reach the backend.
  const backendEnv = { BACKEND_URL };
  if (app.isPackaged) {
    // Packaged build: no npm / source tree available. Run the bundled Next
    // standalone server with Electron's binary acting as Node.
    const serverEntry = path.join(process.resourcesPath, "standalone", "server.js");
    serverProc = spawn(process.execPath, [serverEntry], {
      env: {
        ...process.env,
        ...backendEnv,
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
      env: { ...process.env, ...backendEnv, PORT: String(PORT) },
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

// Polls a URL until it answers (or we give up). Calls onReady either way so a
// slow/unreachable dependency still opens the window (the UI shows an offline
// banner) rather than hanging on a black screen forever.
function waitForServer(url, onReady, attempt = 0) {
  http
    .get(url, (res) => {
      res.resume();
      onReady();
    })
    .on("error", () => {
      if (attempt > 150) {
        console.error(`Server at ${url} did not come up in time; continuing.`);
        onReady();
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

// ─── IPC: data-folder relocation (powers the "Data & storage" panel) ───
// The renderer can't open a native folder picker or restart the process itself
// (contextIsolation + no nodeIntegration), so it delegates both to the main
// process. The backend records the target and moves the files at startup, so the
// relaunch below (which restarts the backend too) is what applies the move.
ipcMain.handle("pick-folder", async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: "Choose a folder for Dataverse AI Forge data",
    properties: ["openDirectory", "createDirectory"],
  });
  if (result.canceled || result.filePaths.length === 0) return null;
  return result.filePaths[0];
});

ipcMain.handle("relaunch", () => {
  app.relaunch();
  app.exit(0);
});

app.whenReady().then(() => {
  // Dev/unpacked on macOS shows the generic Electron dock icon; point it at the
  // brand mark. (The packaged build gets its icon from build-resources/icon.icns.)
  if (process.platform === "darwin" && !app.isPackaged && app.dock) {
    const iconPath = path.join(__dirname, "..", "build-resources", "icon.png");
    const img = nativeImage.createFromPath(iconPath);
    if (!img.isEmpty()) app.dock.setIcon(img);
  }

  // Backend first (so the first paint is connected), then Next pointed at it,
  // then the window.
  ensureBackend(() => {
    startNextServer();
    waitForServer(APP_URL, createWindow);
  });

  // Auto-update (packaged only): checks GitHub Releases, downloads in the
  // background, installs on quit. Works on Windows (nsis) even unsigned; macOS
  // auto-update needs a signed + notarized app (Squirrel.Mac), so it's
  // effectively Windows-only until we sign. Errors are swallowed so an unsigned
  // mac, an offline start, or a missing release feed never blocks launch.
  if (app.isPackaged) {
    autoUpdater.checkForUpdatesAndNotify().catch((err) => {
      console.warn("Auto-update check skipped:", err && err.message ? err.message : err);
    });
  }

  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});

app.on("before-quit", () => {
  app.isQuitting = true;
  // Tear down both children so a relaunch starts from a clean slate (and the
  // backend re-runs its startup data-dir move).
  if (serverProc) serverProc.kill();
  if (backendProc) backendProc.kill();
});
