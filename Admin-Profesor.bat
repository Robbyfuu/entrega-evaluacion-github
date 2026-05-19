@echo off
REM ============================================================
REM  Launcher para Admin-Profesor.ps1
REM  USO EXCLUSIVO DEL PROFESOR.
REM  Doble-click para abrir el panel de control remoto.
REM ============================================================

chcp 65001 >nul 2>&1
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%Admin-Profesor.ps1"

if not exist "%PS1%" (
    echo.
    echo [ERROR] No se encontro Admin-Profesor.ps1 en esta carpeta.
    echo.
    pause
    exit /b 1
)

where powershell >nul 2>&1
if errorlevel 1 (
    echo [ERROR] PowerShell no disponible.
    pause
    exit /b 1
)

REM Desbloquear archivos descargados
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%SCRIPT_DIR%' -File | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%PS1%"

if errorlevel 1 (
    echo.
    echo [ERROR] El script de PowerShell termino con error.
    pause
    exit /b 1
)

endlocal
exit /b 0
