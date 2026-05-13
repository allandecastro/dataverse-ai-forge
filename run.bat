@echo off
REM Double-click shim for run.ps1 - bypasses the default PowerShell execution
REM policy so testers don't have to run "Set-ExecutionPolicy" first.
powershell -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
