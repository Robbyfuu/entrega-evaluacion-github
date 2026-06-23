using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Bloqueo de GitHub Copilot dentro de VS Code sabotizando el settings.json
/// del usuario (%APPDATA%\Code\User\settings.json). No requiere admin: el
/// archivo es del propio usuario.
///
/// Claves que se inyectan para deshabilitar Copilot:
///   "github.copilot.enable": { "*": false }   -> apaga completions en todos los lenguajes
///   "github.copilot.chat.enabled": false      -> apaga el chat de Copilot
///
/// Un FileSystemWatcher detecta si el alumno edita el archivo para reactivar
/// Copilot: re-aplica el sabotaje y dispara OnCheatDetected para que MainWindow
/// reporte el evento y aplique lockdown.
/// </summary>
public static class CopilotBlockService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly string BackupPath = SettingsPath + ".copilot-bak";

    // Claves que inyectamos/quitamos. Si el alumno las borra o las pone en true
    // mientras el watcher esta activo, se dispara OnCheatDetected.
    private const string EnableKey = "github.copilot.enable";
    private const string ChatKey = "github.copilot.chat.enabled";

    private static FileSystemWatcher? _watcher;
    private static Timer? _debounce;
    private static bool _armed;

    /// <summary>
    /// Se dispara cuando el alumno edita el settings.json y quita/cambia las
    /// claves de bloqueo de Copilot. MainWindow suscribe para reportar + lockdown.
    /// Se invoca desde un thread del FileSystemWatcher: el handler debe dispatchear
    /// al UI thread si necesita tocar WPF.
    /// </summary>
    public static event Action? OnCheatDetected;

    public static void Block()
    {
        try
        {
            EnsureDir();
            EnsureDisableKeys();
            StartWatcher();
            _armed = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CopilotBlock] Block fallo: {ex.Message}");
        }
    }

    public static void Unblock()
    {
        try
        {
            StopWatcher();
            _armed = false;
            RemoveDisableKeys();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CopilotBlock] Unblock fallo: {ex.Message}");
        }
    }

    public static bool IsBlocked()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            var json = File.ReadAllText(SettingsPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip
            });
            if (!doc.RootElement.TryGetProperty(EnableKey, out var enable)) return false;
            // {"*": false} -> la propiedad "*" debe existir y ser false
            if (enable.ValueKind != JsonValueKind.Object) return false;
            if (!enable.TryGetProperty("*", out var star)) return false;
            if (star.ValueKind != JsonValueKind.False) return false;
            // Chat key debe ser false (si existe; si no existe lo consideramos bloqueado
            // porque igual lo inyectamos en Block)
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fail-safe igual a InternetBlockService: NO desbloqueamos自动 al iniciar.
    /// Solo logueamos si encontramos un bloqueo huerfano. El primer AdminTick
    /// re-aplica si el profe sigue queriendo bloqueo.
    /// </summary>
    public static void ReconcileOnStartup()
    {
        try
        {
            if (IsBlocked())
                Debug.WriteLine("[CopilotBlock] Bloqueo huerfano encontrado en settings.json (no se desbloquea).");
        }
        catch { }
    }

    // ===================== Escritura del settings.json =====================

    private static void EnsureDir()
    {
        if (!Directory.Exists(SettingsDir))
            Directory.CreateDirectory(SettingsDir);
    }

    /// <summary>
    /// Lee el settings.json (si existe y es JSON valido), mergea las claves de
    /// Copilot, y lo escribe de vuelta preservando el resto de la config del
    /// alumno. Idempotente. Si el archivo esta malformado, lo respalda y crea
    /// uno nuevo con solo las claves de Copilot.
    /// </summary>
    private static void EnsureDisableKeys()
    {
        JsonElement root;
        bool hadFile = File.Exists(SettingsPath);

        if (hadFile)
        {
            try
            {
                var raw = File.ReadAllText(SettingsPath);
                using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip
                });
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // JSON malformado: respaldar y empezar de cero
                try { File.Copy(SettingsPath, BackupPath, overwrite: true); } catch { }
                root = default;
            }
        }
        else
        {
            root = default;
        }

        // Construimos un dict plano desde el root existente (si lo hubo)
        var dict = new Dictionary<string, object?>();
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
                dict[prop.Name] = JsonSerializer.Deserialize<object?>(prop.Value);
        }

        // Claves de Copilot (sobreescriben lo que el alumno tenga)
        dict[EnableKey] = new Dictionary<string, object?> { ["*"] = false };
        dict[ChatKey] = false;

        var opts = new JsonSerializerOptions { WriteIndented = true };
        var jsonOut = JsonSerializer.Serialize(dict, opts);
        WriteWithRetry(jsonOut);
    }

    /// <summary>
    /// Quita SOLO las claves de Copilot del settings.json, preservando todo lo
    /// demas. Si hay un .copilot-bak lo restaura (era el original malformado).
    /// </summary>
    private static void RemoveDisableKeys()
    {
        if (File.Exists(BackupPath))
        {
            try
            {
                File.Move(BackupPath, SettingsPath, overwrite: true);
                return;
            }
            catch { }
        }

        if (!File.Exists(SettingsPath)) return;

        try
        {
            var raw = File.ReadAllText(SettingsPath);
            using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip
            });
            var dict = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name == EnableKey || prop.Name == ChatKey) continue;
                dict[prop.Name] = JsonSerializer.Deserialize<object?>(prop.Value);
            }
            var opts = new JsonSerializerOptions { WriteIndented = true };
            WriteWithRetry(JsonSerializer.Serialize(dict, opts));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CopilotBlock] RemoveDisableKeys fallo: {ex.Message}");
        }
    }

    /// <summary>
    /// Escribe el settings.json con reintentos: VS Code puede tenerlo abierto
    /// brevemente al guardar config desde la UI.
    /// </summary>
    private static void WriteWithRetry(string content, int maxAttempts = 5)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                File.WriteAllText(SettingsPath, content);
                return;
            }
            catch (IOException) when (i < maxAttempts - 1)
            {
                Thread.Sleep(200);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CopilotBlock] WriteWithRetry fallo: {ex.Message}");
                return;
            }
        }
    }

    // ===================== FileSystemWatcher =====================

    private static void StartWatcher()
    {
        StopWatcher();
        try
        {
            EnsureDir();
            _watcher = new FileSystemWatcher(SettingsDir, "settings.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Deleted += OnFileDeleted;
            _watcher.Renamed += OnFileChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CopilotBlock] StartWatcher fallo: {ex.Message}");
        }
    }

    private static void StopWatcher()
    {
        if (_watcher != null)
        {
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnFileChanged;
                _watcher.Deleted -= OnFileDeleted;
                _watcher.Renamed -= OnFileChanged;
                _watcher.Dispose();
            }
            catch { }
            _watcher = null;
        }
        _debounce?.Dispose();
        _debounce = null;
    }

    private static void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (!_armed) return;
        // El alumno borro el settings.json: lo re-creamos con las claves y disparamos cheat
        EnsureDisableKeys();
        FireCheat();
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!_armed) return;
        // Debounce: el watcher puede disparar varias veces por una sola edicion
        _debounce?.Dispose();
        _debounce = new Timer(_ =>
        {
            try
            {
                if (!IsBlocked())
                {
                    // El alumno saco/cambio las claves: re-aplicar y disparar cheat
                    EnsureDisableKeys();
                    FireCheat();
                }
            }
            catch { }
        }, null, 500, Timeout.Infinite);
    }

    private static void FireCheat()
    {
        try { OnCheatDetected?.Invoke(); }
        catch (Exception ex) { Debug.WriteLine($"[CopilotBlock] FireCheat fallo: {ex.Message}"); }
    }
}
