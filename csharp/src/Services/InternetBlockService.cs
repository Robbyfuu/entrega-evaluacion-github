using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Bloqueo/desbloqueo de internet via proxy del usuario (HKCU). No requiere admin.
/// Afecta browsers que respetan el proxy del sistema.
/// </summary>
public static class InternetBlockService
{
    private const string InternetSettingsPath =
        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    private static readonly string[] Browsers =
        { "chrome", "msedge", "firefox", "opera", "brave", "iexplore", "vivaldi", "tor" };

    public static void Block()
    {
        // Cerrar browsers
        foreach (var b in Browsers)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(b))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: true);
            if (key != null)
            {
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", "127.0.0.1:1", RegistryValueKind.String);
                key.SetValue("ProxyOverride", "", RegistryValueKind.String);
            }
            NotifySystem();
        }
        catch { }
    }

    public static void Unblock()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: true);
            if (key != null)
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                key.DeleteValue("ProxyServer", throwOnMissingValue: false);
                key.DeleteValue("ProxyOverride", throwOnMissingValue: false);
            }
            NotifySystem();
        }
        catch { }
    }

    public static bool IsBlocked()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false);
            if (key == null) return false;
            var enabled = key.GetValue("ProxyEnable");
            var server = key.GetValue("ProxyServer") as string;
            return enabled is int e && e == 1 && server != null && server.StartsWith("127.0.0.1");
        }
        catch { return false; }
    }

    private static void NotifySystem()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch { }
    }

    /// <summary>
    /// Al iniciar: si quedo un proxy blackhole puesto (la app crasheo durante un
    /// bloqueo), lo desbloqueamos por seguridad (fail-safe) para no dejar al alumno
    /// sin internet de forma permanente. Si el profe sigue queriendo bloqueo, el
    /// primer AdminTick de MainWindow lo re-aplica en segundos.
    /// </summary>
    public static void ReconcileOnStartup()
    {
        // Si quedo un proxy huerfano de una sesion anterior (la app crasheo con el
        // bloqueo puesto), lo limpiamos por seguridad para no dejar al alumno sin
        // internet de forma permanente. Si el profe sigue queriendo bloqueo, el
        // polling de MainWindow (AdminTick) lo re-aplica en el siguiente tick.
        try
        {
            if (IsBlocked())
                Unblock();
        }
        catch { }
    }
}
