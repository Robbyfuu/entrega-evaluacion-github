@echo off
REM ============================================================
REM  Launcher para Subir-Evaluacion.ps1
REM  Doble-click para abrir la GUI de entrega de evaluación.
REM ============================================================

setlocal

REM Carpeta donde está este .bat (con barra final)
set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%Subir-Evaluacion.ps1"

REM Verificar que el .ps1 exista al lado
if not exist "%PS1%" (
    echo.
    echo [ERROR] No se encontro el archivo:
    echo   %PS1%
    echo.
    echo Asegurate que Subir-Evaluacion.ps1 este en la misma carpeta que este .bat
    echo.
    pause
    exit /b 1
)

REM Verificar que PowerShell exista
where powershell >nul 2>&1
if errorlevel 1 (
    echo.
    echo [ERROR] PowerShell no esta disponible en este sistema.
    echo.
    pause
    exit /b 1
)

REM Lanzar con bypass de execution policy, sin ventana de consola visible
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%PS1%"

REM Si PowerShell falló, mostrar mensaje
if errorlevel 1 (
    echo.
    echo [ERROR] El script de PowerShell termino con error.
    echo.
    pause
    exit /b 1
)

endlocal
exit /b 0
