using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Bloqueo de GitHub Copilot dentro de VS Code sabotizando los settings.json
/// del usuario. No requiere admin: son archivos del propio usuario.
///
/// Claves que se inyectan para deshabilitar Copilot:
///   "chat.disableAIFeatures": true            -> apaga/oculta AI y deshabilita Copilot
///   "github.copilot.enable": { "*": false }   -> apaga completions en todos los lenguajes
///   "github.copilot.chat.enabled": false      -> compatibilidad con VS Code/Copilot viejos
///
/// Un FileSystemWatcher detecta si el alumno edita el archivo para reactivar
/// Copilot: re-aplica el sabotaje y dispara OnCheatDetected para que MainWindow
/// reporte el evento y aplique lockdown.
/// </summary>
public static class CopilotBlockService
{
    private sealed class SettingsTarget
    {
        public SettingsTarget(string label, string settingsDir)
        {
            Label = label;
            SettingsDir = settingsDir;
            SettingsPath = Path.Combine(settingsDir, "settings.json");
            BackupPath = SettingsPath + ".copilot-bak";
        }

        public string Label { get; }
        public string SettingsDir { get; }
        public string SettingsPath { get; }
        public string BackupPath { get; }
    }

    private static readonly string AppData =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    // Claves que inyectamos/quitamos. Si el alumno las borra/cambia mientras
    // el bloqueo esta activo, se re-aplican y se dispara OnCheatDetected.
    private const string DisableAiFeaturesKey = "chat.disableAIFeatures";
    private const string EnableKey = "github.copilot.enable";
    private const string LegacyChatKey = "github.copilot.chat.enabled";

    private static readonly Dictionary<string, object?> DisableSettings = new()
    {
        // Config oficial actual: deshabilita/oculta AI integrada y Copilot.
        [DisableAiFeaturesKey] = true,

        // Cinturon y tirantes: cubre completions, NES, acciones y agentes aunque
        // una version vieja/nueva no respete aun chat.disableAIFeatures.
        [EnableKey] = new Dictionary<string, object?> { ["*"] = false },
        [LegacyChatKey] = false,
        ["github.copilot.nextEditSuggestions.enabled"] = false,
        ["github.copilot.nextEditSuggestions.fixes"] = false,
        ["github.copilot.editor.enableCodeActions"] = false,
        ["github.copilot.renameSuggestions.triggerAutomatically"] = false,
        ["github.copilot.chat.agent.autoFix"] = false,
        ["github.copilot.chat.codesearch.enabled"] = false,
        ["github.copilot.chat.reviewSelection.enabled"] = false,
        ["github.copilot.chat.reviewAgent.enabled"] = false,
        ["chat.agent.enabled"] = false,
        ["chat.commandCenter.enabled"] = false
    };

    private static readonly List<FileSystemWatcher> _watchers = new();
    private static Timer? _debounce;
    private static Timer? _maintenance;
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
            EnsureDisableKeysForAllTargets();
            StartWatcher();
            StartMaintenanceTimer();
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
            RemoveDisableKeysForAllTargets();
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
            var targets = GetSettingsTargets().ToList();
            return targets.Count > 0 && targets.All(IsTargetBlocked);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fail-safe distinto a InternetBlockService: NO desbloqueamos al iniciar.
    /// Solo logueamos si encontramos un bloqueo huerfano. El primer AdminTick
    /// re-aplica si el profe sigue queriendo bloqueo.
    /// </summary>
    public static void ReconcileOnStartup()
    {
        try
        {
            if (IsBlocked())
                Debug.WriteLine("[CopilotBlock] Bloqueo huerfano encontrado en settings.json de VS Code (no se desbloquea).");
        }
        catch { }
    }

    // ===================== Escritura del settings.json =====================

    private static IReadOnlyList<SettingsTarget> GetSettingsTargets()
    {
        var targets = new List<SettingsTarget>();

        // VS Code estable es el objetivo obligatorio en los laboratorios.
        AddProductTargets(targets, "Code", alwaysIncludeDefault: true);

        // Variantes: solo se tocan si existen, para no ensuciar perfiles que el
        // alumno nunca tuvo.
        AddProductTargets(targets, "Code - Insiders", alwaysIncludeDefault: false);
        AddProductTargets(targets, "VSCodium", alwaysIncludeDefault: false);

        return targets
            .GroupBy(t => t.SettingsPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static void AddProductTargets(List<SettingsTarget> targets, string productDirName, bool alwaysIncludeDefault)
    {
        var userDir = Path.Combine(AppData, productDirName, "User");
        var productExists = Directory.Exists(Path.Combine(AppData, productDirName));

        if (alwaysIncludeDefault || productExists)
            targets.Add(new SettingsTarget($"{productDirName}/default", userDir));

        var profilesDir = Path.Combine(userDir, "profiles");
        if (!Directory.Exists(profilesDir)) return;

        foreach (var profileDir in Directory.GetDirectories(profilesDir))
            targets.Add(new SettingsTarget($"{productDirName}/profile/{Path.GetFileName(profileDir)}", profileDir));
    }

    private static void EnsureDir(SettingsTarget target)
    {
        if (!Directory.Exists(target.SettingsDir))
            Directory.CreateDirectory(target.SettingsDir);
    }

    private static void EnsureDisableKeysForAllTargets()
    {
        foreach (var target in GetSettingsTargets())
            EnsureDisableKeys(target);
    }

    /// <summary>
    /// Lee el settings.json (si existe y es JSON valido), mergea las claves de
    /// Copilot, y lo escribe de vuelta preservando el resto de la config del
    /// alumno. Idempotente. Si el archivo esta malformado, lo respalda y crea
    /// uno nuevo con solo las claves de Copilot.
    /// </summary>
    private static void EnsureDisableKeys(SettingsTarget target)
    {
        JsonElement root;
        bool hadFile = File.Exists(target.SettingsPath);

        if (hadFile)
        {
            try
            {
                var raw = File.ReadAllText(target.SettingsPath);
                using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip
                });
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // JSON malformado: respaldar y empezar de cero
                try { File.Copy(target.SettingsPath, target.BackupPath, overwrite: true); } catch { }
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

        // Claves de Copilot/AI (sobreescriben lo que el alumno tenga)
        foreach (var setting in DisableSettings)
            dict[setting.Key] = setting.Value;

        var opts = new JsonSerializerOptions { WriteIndented = true };
        var jsonOut = JsonSerializer.Serialize(dict, opts);
        WriteWithRetry(target, jsonOut);
    }

    private static bool IsTargetBlocked(SettingsTarget target)
    {
        if (!File.Exists(target.SettingsPath)) return false;

        var json = File.ReadAllText(target.SettingsPath);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip
        });

        foreach (var expected in DisableSettings)
        {
            if (!doc.RootElement.TryGetProperty(expected.Key, out var actual)) return false;

            if (expected.Key == EnableKey)
            {
                if (actual.ValueKind != JsonValueKind.Object) return false;
                if (!actual.TryGetProperty("*", out var star)) return false;
                if (star.ValueKind != JsonValueKind.False) return false;
                continue;
            }

            if (expected.Value is bool expectedBool)
            {
                if (expectedBool && actual.ValueKind != JsonValueKind.True) return false;
                if (!expectedBool && actual.ValueKind != JsonValueKind.False) return false;
            }
        }

        return true;
    }

    private static void RemoveDisableKeysForAllTargets()
    {
        foreach (var target in GetSettingsTargets())
            RemoveDisableKeys(target);
    }

    /// <summary>
    /// Quita SOLO las claves de Copilot del settings.json, preservando todo lo
    /// demas. Si hay un .copilot-bak lo restaura (era el original malformado).
    /// </summary>
    private static void RemoveDisableKeys(SettingsTarget target)
    {
        if (File.Exists(target.BackupPath))
        {
            try
            {
                File.Move(target.BackupPath, target.SettingsPath, overwrite: true);
                return;
            }
            catch { }
        }

        if (!File.Exists(target.SettingsPath)) return;

        try
        {
            var raw = File.ReadAllText(target.SettingsPath);
            using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip
            });
            var dict = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (DisableSettings.ContainsKey(prop.Name)) continue;
                dict[prop.Name] = JsonSerializer.Deserialize<object?>(prop.Value);
            }
            var opts = new JsonSerializerOptions { WriteIndented = true };
            WriteWithRetry(target, JsonSerializer.Serialize(dict, opts));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CopilotBlock] RemoveDisableKeys fallo en {target.Label}: {ex.Message}");
        }
    }

    /// <summary>
    /// Escribe el settings.json con reintentos: VS Code puede tenerlo abierto
    /// brevemente al guardar config desde la UI.
    /// </summary>
    private static void WriteWithRetry(SettingsTarget target, string content, int maxAttempts = 5)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                EnsureDir(target);
                File.WriteAllText(target.SettingsPath, content);
                return;
            }
            catch (IOException) when (i < maxAttempts - 1)
            {
                Thread.Sleep(200);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CopilotBlock] WriteWithRetry fallo en {target.Label}: {ex.Message}");
                return;
            }
        }
    }

    // ===================== FileSystemWatcher =====================

    private static void StartWatcher()
    {
        StopWatcher();
        foreach (var target in GetSettingsTargets())
        {
            try
            {
                EnsureDir(target);
                var watcher = new FileSystemWatcher(target.SettingsDir, "settings.json")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };
                watcher.Changed += OnFileChanged;
                watcher.Deleted += OnFileDeleted;
                watcher.Renamed += OnFileChanged;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CopilotBlock] StartWatcher fallo en {target.Label}: {ex.Message}");
            }
        }
    }

    private static void StopWatcher()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnFileChanged;
                watcher.Deleted -= OnFileDeleted;
                watcher.Renamed -= OnFileChanged;
                watcher.Dispose();
            }
            catch { }
        }
        _watchers.Clear();
        _debounce?.Dispose();
        _debounce = null;
        _maintenance?.Dispose();
        _maintenance = null;
    }

    private static void StartMaintenanceTimer()
    {
        _maintenance?.Dispose();
        _maintenance = new Timer(_ =>
        {
            if (!_armed) return;

            try
            {
                if (!IsBlocked())
                {
                    EnsureDisableKeysForAllTargets();
                    FireCheat();
                    StartWatcher();
                    StartMaintenanceTimer();
                }
            }
            catch { }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private static void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (!_armed) return;
        // El alumno borro el settings.json: lo re-creamos con las claves y disparamos cheat
        EnsureDisableKeysForAllTargets();
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
                    EnsureDisableKeysForAllTargets();
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
