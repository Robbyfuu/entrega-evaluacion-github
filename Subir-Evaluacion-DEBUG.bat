@echo off
REM ============================================================
REM  LAUNCHER DE DEBUG - muestra errores en pantalla
REM  Usar SOLO si Subir-Evaluacion.bat normal no abre la app.
REM ============================================================

chcp 65001 >nul 2>&1
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%Subir-Evaluacion.ps1"

if not exist "%PS1%" (
    echo.
    echo [ERROR] No se encontro Subir-Evaluacion.ps1
    echo.
    pause
    exit /b 1
)

echo ==================================================
echo  MODO DEBUG - ventana visible, errores mostrados
echo ==================================================
echo.
echo Si la app no se abrio antes, mira los mensajes de error
echo que aparezcan abajo y compartelos con el profesor.
echo.
echo ==================================================
echo.

REM Desbloquear archivos
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%SCRIPT_DIR%' -File | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

REM Ejecutar PS1 SIN -WindowStyle Hidden, ventana visible
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS1%"

echo.
echo ==================================================
echo Script terminado. Codigo de salida: %ERRORLEVEL%
echo ==================================================
pause
endlocal
