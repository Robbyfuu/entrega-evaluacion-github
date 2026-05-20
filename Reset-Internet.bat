@echo off
REM ============================================================
REM  RESCATE: limpia el proxy del usuario si quedo bloqueado.
REM  Usar SOLO si el script principal no esta abierto y el proxy
REM  esta bloqueado. Doble-click para ejecutar.
REM ============================================================

chcp 65001 >nul 2>&1
echo.
echo ==================================================
echo  Reset de proxy del usuario (HKCU)
echo ==================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -Name 'ProxyEnable' -Value 0 -Type DWord -Force; Write-Host 'ProxyEnable = 0 (desactivado)' -ForegroundColor Green; Write-Host 'Internet desbloqueado.' -ForegroundColor Green"

if errorlevel 1 (
    echo.
    echo [ERROR] No se pudo limpiar el proxy.
    pause
    exit /b 1
)

echo.
echo Listo. Reinicia el navegador para que tome la nueva config.
echo.
pause
