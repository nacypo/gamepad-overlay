@echo off
setlocal
rem ============================================================
rem  Gamepad Overlay - desktop launcher
rem  Runs GamepadOverlay.exe (WebView2 host) in this folder.
rem  Extra arguments are forwarded, e.g.:
rem     launcher.bat --anchor=tl --w=760 --h=540
rem     launcher.bat --opaque            (solid bg, for OBS window capture)
rem     launcher.bat --no-click-through  (mouse can click the overlay)
rem ============================================================
cd /d "%~dp0"

if not exist "GamepadOverlay.exe" (
  echo [x] GamepadOverlay.exe not found.
  pause
  exit /b 1
)

start "" "%~dp0GamepadOverlay.exe" %*

timeout /t 2 /nobreak >nul
tasklist /fi "imagename eq GamepadOverlay.exe" | find /i "GamepadOverlay.exe" >nul
if errorlevel 1 (
  echo.
  echo [!] Overlay did not stay running.
  echo     Make sure the Microsoft Edge WebView2 Runtime is installed.
  pause
)
endlocal
