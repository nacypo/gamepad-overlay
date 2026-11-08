@echo off
rem ============================================================
rem   One-click launcher: RivaTuner Statistics Server + overlay
rem   Both are skipped if already running.
rem ============================================================
setlocal
set "HERE=%~dp0"
set "RTSS=D:\RivaTuner Statistics Server\RTSS.exe"
if not exist "%RTSS%" set "RTSS=%ProgramFiles%\RivaTuner Statistics Server\RTSS.exe"
if not exist "%RTSS%" set "RTSS=%ProgramFiles(x86)%\RivaTuner Statistics Server\RTSS.exe"

rem --- 1) RTSS ---
tasklist /fi "imagename eq RTSS.exe" 2>nul | find /i "RTSS.exe" >nul
if errorlevel 1 (
  if exist "%RTSS%" (
    start "" "%RTSS%" /s
    timeout /t 2 /nobreak >nul
  )
)

rem --- 2) overlay ---
tasklist /fi "imagename eq GamepadOverlay.exe" 2>nul | find /i "GamepadOverlay.exe" >nul
if errorlevel 1 (
  start "" "%HERE%GamepadOverlay.exe"
)

exit /b 0
