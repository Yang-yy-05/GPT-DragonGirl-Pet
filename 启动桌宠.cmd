@echo off
chcp 65001 >nul
cd /d "%~dp0"
if exist "发布\GPT小龙\GPTPet.exe" (
  start "" "发布\GPT小龙\GPTPet.exe"
) else (
  echo Please run build.ps1 with PowerShell first.
  pause
)
