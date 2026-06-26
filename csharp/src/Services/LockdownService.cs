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
    // Verificacion del password del profe: PBKDF2-HMAC-SHA256 sobre SHA256(pass),
    // con salt + 200k iteraciones. El plaintext NO esta en el codigo. El SHA256
    // interno mantiene el formato con que se genera el hash; PBKDF2 + salt encarece
    // el cracking offline ~200.000x si alguien extrae estos valores del binario.
    // (Sigue siendo client-side: el desbloqueo confiable es por panel / pc_overrides.)
    private const string TeacherPwSalt = "d32a46ee2bbf89786a970cf26d98df1e";
    private const int TeacherPwIterations = 200000;
    private const string TeacherPwHash =
        "64387bbc78763feb3a635492d0e26b04c29f722cfde707a8621424a0d8451ee3";

    private static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EntregaEvaluacion");

    private static string MarkerFile => Path.Combine(AppDataDir, ".cheat-detected.json");

    private const string RunRegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunRegName = "EntregaEvaluacionLock";
    private const string PoliciesPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";

    public static bool VerifyPassword(string candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        var inner = SHA256.HashData(Encoding.UTF8.GetBytes(candidate));
        var salt = Convert.FromHexString(TeacherPwSalt);
        var derived = Rfc2898DeriveBytes.Pbkdf2(inner, salt, TeacherPwIterations, HashAlgorithmName.SHA256, 32);
        var expected = Convert.FromHexString(TeacherPwHash);
        return CryptographicOperations.FixedTimeEquals(derived, expected);
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
