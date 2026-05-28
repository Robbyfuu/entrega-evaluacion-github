using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Lockdown persistente anti-trampa: marker file, auto-arranque, deshabilitar
/// Task Manager. Liberacion solo con clave del profesor.
/// </summary>
public static class LockdownService
{
    // Hash SHA-256 del password del profesor. Password real NO esta en el codigo.
    private const string TeacherPasswordHash =
        "203ed3a8347bae6d9659e8830f4f5b882828e91b5249f63d61392ead80ec2d74";

    private static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EntregaEvaluacion");

    private static string MarkerFile => Path.Combine(AppDataDir, ".cheat-detected.json");

    private const string RunRegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunRegName = "EntregaEvaluacionLock";
    private const string PoliciesPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";

    public static bool VerifyPassword(string candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        var bytes = Encoding.UTF8.GetBytes(candidate);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return hash == TeacherPasswordHash;
    }

    public static bool HasPersistentMarker() => File.Exists(MarkerFile);

    public static void Trigger(string repoName, int filesCount, string[] filesNames)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var data = System.Text.Json.JsonSerializer.Serialize(new
            {
                repo = repoName,
                count = filesCount,
                files = filesNames,
                date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
            File.WriteAllText(MarkerFile, data);
        }
        catch { }

        // Copiar el exe a AppData para auto-arranque persistente
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null && File.Exists(exePath))
            {
                var dest = Path.Combine(AppDataDir, "EntregaEvaluacion-Lock.exe");
                File.Copy(exePath, dest, overwrite: true);
                using var run = Registry.CurrentUser.CreateSubKey(RunRegPath);
                run?.SetValue(RunRegName, $"\"{dest}\"");
            }
        }
        catch { }

        // Deshabilitar Task Manager
        try
        {
            using var pol = Registry.CurrentUser.CreateSubKey(PoliciesPath);
            pol?.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
        }
        catch { }
    }

    public static void Release()
    {
        try { File.Delete(MarkerFile); } catch { }
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunRegPath, writable: true);
            run?.DeleteValue(RunRegName, throwOnMissingValue: false);
        }
        catch { }
        try
        {
            using var pol = Registry.CurrentUser.OpenSubKey(PoliciesPath, writable: true);
            pol?.DeleteValue("DisableTaskMgr", throwOnMissingValue: false);
        }
        catch { }
        try
        {
            var lockExe = Path.Combine(AppDataDir, "EntregaEvaluacion-Lock.exe");
            File.Delete(lockExe);
        }
        catch { }
    }
}
