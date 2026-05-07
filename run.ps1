#requires -Version 5.1
<#
.SYNOPSIS
    Starts the Dataverse AI Forge backend + frontend for local testing.

.DESCRIPTION
    Designed for non-developer testers on Windows. Verifies prerequisites,
    starts the .NET API and the Next.js dev server in their own windows, and
    opens the browser to http://localhost:3000 once the backend is healthy.

    The two child windows persist until you close them — that's where the
    backend / frontend logs go. Close either window to stop that side.

.PARAMETER NoBrowser
    Skip the browser-launch step. Useful if you want to use a different
    browser or if 3000 is already open.

.PARAMETER SkipNpmInstall
    Skip "npm install" on the frontend. Use this when you've already
    installed dependencies and want a faster restart.

.NOTES
    Requirements:
      - .NET 10 SDK (10.0.102+)  https://dotnet.microsoft.com/download
      - Node.js 22+              https://nodejs.org/

    First-run guide for testers is in README.md at the top.
#>

[CmdletBinding()]
param(
    [switch]$NoBrowser,
    [switch]$SkipNpmInstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step($message) {
    Write-Host ""
    Write-Host "→ $message" -ForegroundColor Cyan
}

function Write-Ok($message) {
    Write-Host "  ✓ $message" -ForegroundColor Green
}

function Fail($message) {
    Write-Host ""
    Write-Host "✗ $message" -ForegroundColor Red
    Write-Host ""
    Write-Host "Press any key to close..." -ForegroundColor Yellow
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    exit 1
}

# ─── 1. Prerequisite check ──────────────────────────────────────────────
Write-Step "Checking prerequisites"

# .NET 10 — `dotnet --version` succeeds with any installed SDK; we want >= 10.
$dotnetVersion = $null
try { $dotnetVersion = & dotnet --version 2>$null } catch { }
if (-not $dotnetVersion) {
    Fail ".NET SDK not found. Install .NET 10 from https://dotnet.microsoft.com/download and re-run this script."
}
$dotnetMajor = ($dotnetVersion -split '\.')[0] -as [int]
if ($dotnetMajor -lt 10) {
    Fail ".NET SDK $dotnetVersion is too old. This project needs .NET 10 (10.0.102+). Install from https://dotnet.microsoft.com/download."
}
Write-Ok ".NET SDK $dotnetVersion"

# Node — `node --version` returns vX.Y.Z; we want X >= 22.
$nodeVersion = $null
try { $nodeVersion = & node --version 2>$null } catch { }
if (-not $nodeVersion) {
    Fail "Node.js not found. Install Node 22+ from https://nodejs.org/ and re-run this script."
}
$nodeMajor = ($nodeVersion -replace '^v','' -split '\.')[0] -as [int]
if ($nodeMajor -lt 22) {
    Fail "Node $nodeVersion is too old. This project needs Node 22+. Install from https://nodejs.org/."
}
Write-Ok "Node $nodeVersion"

# ─── 2. Frontend dependencies ─────────────────────────────────────────
$frontendDir = Join-Path $repoRoot 'frontend'
$nodeModules = Join-Path $frontendDir 'node_modules'

if (-not $SkipNpmInstall) {
    if (-not (Test-Path $nodeModules)) {
        Write-Step "Installing frontend dependencies (first run takes ~1 minute)"
        Push-Location $frontendDir
        try {
            & npm install
            if ($LASTEXITCODE -ne 0) { Fail "npm install failed (exit $LASTEXITCODE). Check the output above." }
        } finally {
            Pop-Location
        }
        Write-Ok "Dependencies installed"
    } else {
        Write-Ok "Frontend dependencies already installed (use -SkipNpmInstall on future runs to skip the check entirely)"
    }
}

# ─── 3. Launch backend + frontend in their own windows ────────────────
$backendDir = Join-Path $repoRoot 'backend'

Write-Step "Starting backend (.NET API on http://localhost:5000)"
$backendCmd = "Set-Location '$backendDir'; Write-Host 'Dataverse AI Forge — backend' -ForegroundColor Cyan; dotnet run --project src/DataverseForge.Api"
Start-Process -FilePath 'powershell' -ArgumentList '-NoExit', '-Command', $backendCmd -WindowStyle Normal | Out-Null
Write-Ok "Backend window opened"

Write-Step "Starting frontend (Next.js dev server on http://localhost:3000)"
$frontendCmd = "Set-Location '$frontendDir'; Write-Host 'Dataverse AI Forge — frontend' -ForegroundColor Cyan; npm run dev"
Start-Process -FilePath 'powershell' -ArgumentList '-NoExit', '-Command', $frontendCmd -WindowStyle Normal | Out-Null
Write-Ok "Frontend window opened"

# ─── 4. Wait for backend, then open the browser ───────────────────────
if (-not $NoBrowser) {
    Write-Step "Waiting for backend to come up"
    $deadline = (Get-Date).AddSeconds(60)
    $up = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-WebRequest -Uri 'http://localhost:5000/healthz' -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
            if ($resp.StatusCode -eq 200) { $up = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if ($up) {
        Write-Ok "Backend healthy"
        Write-Step "Opening http://localhost:3000 in your default browser"
        Start-Process 'http://localhost:3000'
    } else {
        Write-Host "  ! Backend didn't become healthy within 60s — check the backend window for errors." -ForegroundColor Yellow
        Write-Host "    You can still open http://localhost:3000 manually once it's ready." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor DarkGray
Write-Host "  Dataverse AI Forge is running." -ForegroundColor Green
Write-Host "  • Backend:  http://localhost:5000"
Write-Host "  • Frontend: http://localhost:3000"
Write-Host ""
Write-Host "  Two PowerShell windows are now open — one per service. Close either"
Write-Host "  window to stop that side."
Write-Host ""
Write-Host "  First time using it? Click the gear icon (top-right) and fill in"
Write-Host "  your Azure AI + Dataverse credentials, then click Test on each."
Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor DarkGray
Write-Host ""
