@echo off
REM ============================================================
REM  RESCATE: limpia el proxy del usuario si quedo bloqueado.
REM  Usar SOLO si el script principal no esta abierto y el proxy
REM  esta bloqueado. Doble-click para ejecutar.
REM ============================================================

chcp 65001 >nul 2>&1
echo.
echo ==================================================
echo  Reset de proxy del usuario (HKCU) - completo
echo ==================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $r='HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'; Set-ItemProperty -Path $r -Name 'ProxyEnable' -Value 0 -Type DWord -Force; Remove-ItemProperty -Path $r -Name 'ProxyServer' -ErrorAction SilentlyContinue; Remove-ItemProperty -Path $r -Name 'ProxyOverride' -ErrorAction SilentlyContinue; try { if (-not ('Win32.WinInet' -as [type])) { Add-Type -MemberDefinition '[DllImport(\"wininet.dll\")] public static extern bool InternetSetOption(IntPtr h, int o, IntPtr b, int l);' -Name 'WinInet' -Namespace 'Win32' | Out-Null }; [Win32.WinInet]::InternetSetOption([IntPtr]::Zero, 39, [IntPtr]::Zero, 0) | Out-Null; [Win32.WinInet]::InternetSetOption([IntPtr]::Zero, 37, [IntPtr]::Zero, 0) | Out-Null } catch {}; Write-Host 'Proxy limpiado y sistema notificado.' -ForegroundColor Green; Write-Host 'Internet desbloqueado.' -ForegroundColor Green }"

if errorlevel 1 (
    echo.
    echo [ERROR] No se pudo limpiar el proxy.
    pause
    exit /b 1
)

echo.
echo Listo. Si tu navegador todavia falla, cierralo completamente y vuelvelo a abrir.
echo.
pause
