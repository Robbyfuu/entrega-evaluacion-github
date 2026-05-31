using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using EntregaEvaluacion.Models;
using EntregaEvaluacion.Services;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Ventana principal (equivalente a MainForm). Conserva EXACTA la logica de
/// negocio: sesion, modo crear/existente, crear/clonar repo, subir, banner de
/// Classroom, y polling admin (config, heartbeat, lockdown dirigido/remoto).
/// Solo cambia la capa UI (WPF + WPF-UI, layout fluido en lugar de coords).
/// </summary>
public partial class MainWindow : Window
{
    private readonly GitHubService _gh = new();
    private readonly SupabaseClient _sb = new();

    // Estado
    private GitHubUser? _user;
    private bool _internetBlocked;
    private bool _remoteLockdownActive;
    private bool _targetedLockdownActive;
    private string _lastAdminMessage = "";
    private readonly HashSet<string> _lastProcSet = new();

    private DispatcherTimer _adminTimer = null!;

    // Evita disparar handlers durante la carga inicial de combos.
    private bool _initializing = true;

    public MainWindow()
    {
        InitializeComponent();

        // Poblar combos (equivalente a Items.AddRange en WinForms)
        foreach (var s in Config.Sections) SectionCombo.Items.Add(s);
        foreach (var t in Config.EvaluationTypes) TipoCombo.Items.Add(t);

        Loaded += async (_, _) => await InitAsync();
    }

    // ===================== Init =====================
    private async Task InitAsync()
    {
        Log("Listo. Completa los datos y elige una accion.");

        // Chequear update en background (no bloquea el arranque). Si hay update,
        // descarga + reinicia. Silencioso si no hay o sin internet.
        _ = Task.Run(async () =>
        {
            await UpdateService.CheckAndApplyAsync(msg => Log(msg));
        });

        // Seccion guardada o pedir
        _initializing = true;
        var saved = StudentSection.Get();
        if (!string.IsNullOrEmpty(saved) && Config.Sections.Contains(saved))
            SectionCombo.SelectedItem = saved;
        else
            PromptSection();
        _initializing = false;

        await UpdateSessionPanel();
        SetModoUi();
        UpdateButtonStates();
        await UpdateAssignmentsBanner();

        // Timer admin
        _adminTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Config.PollIntervalMs) };
        _adminTimer.Tick += async (_, _) => await AdminTickAsync();
        _adminTimer.Start();
        await AdminTickAsync();
    }

    private void PromptSection()
    {
        var dlg = new SectionPromptWindow { Owner = this };
        dlg.ShowDialog();
        var sel = dlg.SelectedSection;
        SectionCombo.SelectedItem = sel;
        StudentSection.Set(sel);
    }

    // ===================== Eventos UI =====================
    private void SectionCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing || SectionCombo.SelectedItem == null) return;
        StudentSection.Set((string)SectionCombo.SelectedItem);
        _ = UpdateAssignmentsBanner();
    }

    private async void AssignmentsLink_Click(object sender, RoutedEventArgs e) => await ShowAssignmentsDialog();

    private async void LoginButton_Click(object sender, RoutedEventArgs e) => await DoLoginAsync();

    private async void LogoutButton_Click(object sender, RoutedEventArgs e) => await DoLogoutAsync();

    private void ModoNuevo_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        SetModoUi();
    }

    private async void ModoExistente_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        SetModoUi();
        await LoadUserReposAsync();
    }

    private void NombreBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_initializing) return;
        UpdateRepoPreview();
        UpdateButtonStates();
    }

    private void TipoCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        UpdateRepoPreview();
        UpdateButtonStates();
    }

    private void ReposCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        UpdateRepoPreview();
        UpdateButtonStates();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadUserReposAsync();

    private void BuscarButton_Click(object sender, RoutedEventArgs e) => BuscarCarpeta();

    private async void CrearButton_Click(object sender, RoutedEventArgs e) => await CrearRepoAsync();

    private async void SubirButton_Click(object sender, RoutedEventArgs e) => await SubirArchivosAsync();

    // ===================== Sesion =====================
    private async Task UpdateSessionPanel()
    {
        if (_gh.IsAuthenticated)
        {
            _user = await _gh.GetUserAsync();
            if (_user != null)
            {
                SessionUserText.Text = "@" + _user.Login;
                SessionUserText.Foreground = Brushes.DarkGreen;
                SessionEmailText.Text = _user.Email ?? "(email privado)";
                LoginButton.IsEnabled = false;
                LogoutButton.IsEnabled = true;
            }
        }
        else
        {
            _user = null;
            SessionUserText.Text = "Sin sesion";
            SessionUserText.Foreground = Brushes.Gray;
            SessionEmailText.Text = "(no conectado)";
            LoginButton.IsEnabled = true;
            LogoutButton.IsEnabled = false;
        }
        UpdateButtonStates();
    }

    private async Task DoLoginAsync()
    {
        Log("-> Iniciando sesion con codigo...");
        var dlg = new LoginWindow(_gh) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            await UpdateSessionPanel();
            Log("Sesion iniciada.");
        }
    }

    private async Task DoLogoutAsync()
    {
        if (!_gh.IsAuthenticated) return;
        var r = MessageBox.Show("Cerrar sesion y borrar credenciales de este equipo?", "Cerrar sesion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        _gh.Logout();
        Log("Sesion cerrada.");
        await UpdateSessionPanel();
    }

    // ===================== Modo UI =====================
    private void SetModoUi()
    {
        if (ModoExistente.IsChecked == true)
        {
            ReposCombo.IsEnabled = true; RefreshButton.IsEnabled = true;
            NombreBox.IsEnabled = false; TipoCombo.IsEnabled = false;
            CrearButton.Content = "1. Clonar Repo";
        }
        else
        {
            ReposCombo.IsEnabled = false; RefreshButton.IsEnabled = false;
            NombreBox.IsEnabled = true; TipoCombo.IsEnabled = true;
            CrearButton.Content = "1. Crear Repo";
        }
        UpdateRepoPreview();
    }

    private static string Sanitize(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        var clean = sb.ToString().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
        clean = Regex.Replace(clean, @"\s+", "-");
        clean = Regex.Replace(clean, @"[^a-z0-9\-]", "");
        clean = Regex.Replace(clean, @"-+", "-").Trim('-');
        return clean;
    }

    private string? GetRepoName()
    {
        if (ModoExistente.IsChecked == true)
        {
            if (ReposCombo.SelectedItem == null) return null;
            return Regex.Replace((string)ReposCombo.SelectedItem, @"^\S+\s+", "").Trim();
        }
        var n = NombreBox.Text.Trim();
        var t = (TipoCombo.SelectedItem as string ?? "").Trim();
        if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(t)) return null;
        return Sanitize($"{n}-{t}");
    }

    private void UpdateRepoPreview()
    {
        var repo = GetRepoName();
        if (repo != null) { RepoDestinoText.Text = repo; RepoDestinoText.Foreground = Brushes.Black; }
        else { RepoDestinoText.Text = ModoExistente.IsChecked == true ? "(selecciona un repositorio)" : "(rellenar nombre y tipo)"; RepoDestinoText.Foreground = Brushes.Gray; }
    }

    private void UpdateButtonStates()
    {
        var hasAuth = _gh.IsAuthenticated;
        var hasFolder = !string.IsNullOrEmpty(CarpetaBox.Text) && Directory.Exists(CarpetaBox.Text);
        var hasRepoData = ModoExistente.IsChecked == true ? ReposCombo.SelectedItem != null
            : (!string.IsNullOrEmpty(NombreBox.Text.Trim()) && !string.IsNullOrEmpty((TipoCombo.SelectedItem as string ?? "").Trim()));
        CrearButton.IsEnabled = hasAuth && hasRepoData;
        SubirButton.IsEnabled = hasAuth && hasRepoData && hasFolder;
    }

    private void BuscarCarpeta()
    {
        var fbd = new OpenFolderDialog { Title = "Selecciona la carpeta con tu evaluacion" };
        if (fbd.ShowDialog() == true)
        {
            CarpetaBox.Text = fbd.FolderName;
            Log($"Carpeta: {fbd.FolderName}");
            UpdateButtonStates();
        }
    }

    // ===================== Repos =====================
    private async Task LoadUserReposAsync()
    {
        if (!_gh.IsAuthenticated) { Log("Inicia sesion primero."); return; }
        Log("-> Cargando repos (incluye Classroom)...");
        ReposCombo.Items.Clear();
        var repos = await _gh.ListReposAsync();
        var me = _user?.Login ?? "";
        var sorted = repos.Where(r => !r.Archived)
            .OrderBy(r => r.Owner?.Login == me ? 1 : 0).ThenBy(r => r.FullName);
        int count = 0;
        foreach (var r in sorted)
        {
            var vis = r.Private ? "[Priv]" : "[Pub]";
            var disp = r.Owner?.Login != me ? $"{vis} {r.FullName}" : $"{vis} {r.Name}";
            ReposCombo.Items.Add(disp);
            count++;
        }
        Log($"OK {count} repos cargados.");
        Status($"Repos disponibles: {count}");

        if (count == 0)
        {
            // Chequear invitaciones pendientes SIEMPRE
            var invites = await _gh.GetPendingInvitationsAsync();
            if (invites.Count > 0)
            {
                Log($"Tienes {invites.Count} invitacion(es) pendiente(s).");
                if (await AcceptInvitationsAsync(invites)) { await Task.Delay(2000); await LoadUserReposAsync(); return; }
            }
            var asg = await _sb.GetActiveAssignmentsAsync();
            asg = FilterBySection(asg);
            if (asg.Count > 0)
                Log($"Tienes {asg.Count} tarea(s) Classroom. Usa el banner para aceptarlas.");
            else
                Log("Sin assignments para tu seccion. Pregunta al profesor.");
        }
    }

    private async Task<bool> AcceptInvitationsAsync(List<RepoInvitation> invites)
    {
        var list = string.Join("\n", invites.Select(i => $"  - {i.Repository?.FullName} (de @{i.Inviter?.Login})"));
        var r = MessageBox.Show($"Tienes {invites.Count} invitacion(es):\n\n{list}\n\nAceptar todas?", "Invitaciones pendientes", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return false;
        int ok = 0;
        var urls = new List<string>();
        foreach (var inv in invites)
        {
            if (await _gh.AcceptInvitationAsync(inv.Id)) { ok++; if (inv.Repository != null) urls.Add($"https://github.com/{inv.Repository.FullName}"); }
        }
        if (ok > 0)
        {
            try { Clipboard.SetText(string.Join("\n", urls)); } catch { }
            MessageBox.Show($"Se aceptaron {ok} invitacion(es).\n\nURL copiada al portapapeles:\n{string.Join("\n", urls)}\n\nPega el enlace en la evaluacion correspondiente del AVA.", "Listo", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        return false;
    }

    // ===================== Crear / Clonar =====================
    private async Task CrearRepoAsync()
    {
        if (!_gh.IsAuthenticated) { Log("Inicia sesion primero."); return; }
        var repo = GetRepoName();
        if (repo == null) { MessageBox.Show("Faltan datos.", "Atencion", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        if (ModoExistente.IsChecked == true)
        {
            await CloneExistingAsync(repo);
            return;
        }

        Status($"Creando repo {repo}...");
        Log($"-> Creando repo '{repo}' (publico)");
        var existing = await _gh.GetRepoAsync(_user!.Login, repo);
        if (existing != null) { Log("Repo ya existe, se reutiliza."); }
        else
        {
            var created = await _gh.CreateRepoAsync(repo, isPublic: true);
            if (!created) { Log("Error creando repo."); MessageBox.Show("No se pudo crear el repo.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            Log("OK Repo creado.");
        }

        var url = $"https://github.com/{_user!.Login}/{repo}";
        var folder = CarpetaBox.Text;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) OpenFolder(folder);
        MessageBox.Show($"Repositorio creado correctamente.\n\nURL: {url}\n\nProximo paso: 'Subir Archivos'.", "Repositorio creado", MessageBoxButton.OK, MessageBoxImage.Information);
        await _sb.ReportStudentActivityAsync("create_repo", _user!.Login, _user.Email, Environment.MachineName, StudentSection.Get(), repo, url);
        UpdateButtonStates();
    }

    private async Task CloneExistingAsync(string repo)
    {
        Log($"-> Validando repo '{repo}'...");
        // Parsear owner/nombre
        string owner, name;
        if (repo.Contains('/')) { var p = repo.Split('/', 2); owner = p[0]; name = p[1]; }
        else { owner = _user!.Login; name = repo; }

        var rep = await _gh.GetRepoAsync(owner, name);
        if (rep == null) { Log("No se pudo acceder al repo."); MessageBox.Show("No se encontro el repo. Refresca o crea uno nuevo.", "Atencion", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var target = Path.Combine(desktop, name);

        var git = new GitService(_gh.Token!, _user!.Login, _user.Email ?? "");
        Status($"Clonando {name}...");
        var res = git.Clone(owner, name, target);
        if (!res.Ok) { Log($"Fallo el clone: {res.Error}"); return; }

        if (res.Reused)
        {
            Log("Carpeta reutilizada (anti-trampa NO se ejecuta).");
        }
        else
        {
            // Anti-trampa solo en clone fresco
            var clean = GitService.TestRepoIsClean(target);
            if (!clean.IsClean)
            {
                Log($"TRAMPA: {clean.FilesCount} archivo(s) no permitidos.");
                try { Directory.Delete(target, true); } catch { }
                CarpetaBox.Text = "";
                LockdownService.Trigger(repo, clean.FilesCount, clean.FilesNames);
                await _sb.ReportCheatEventAsync(_user!.Login, Environment.MachineName, repo, clean.FilesCount, clean.FilesNames);
                var alert = new CheatWindow(repo, clean.FilesCount, clean.FilesNames);
                alert.ShowDialog();
                UpdateButtonStates();
                return;
            }
            Log("OK Repo limpio.");
        }

        CarpetaBox.Text = target;
        OpenPythonIdle(target);
        await _sb.ReportStudentActivityAsync("clone", _user!.Login, _user.Email, Environment.MachineName, StudentSection.Get(), repo, $"https://github.com/{owner}/{name}");
        MessageBox.Show($"Repo clonado en:\n{target}\n\nSe abrio IDLE de Python.\n\nEdita, guarda (Ctrl+S), y luego 'Subir Archivos'.", "Listo", MessageBoxButton.OK, MessageBoxImage.Information);
        Status("Edita en IDLE y luego Subir Archivos.");
        UpdateButtonStates();
    }

    // ===================== Subir =====================
    private async Task SubirArchivosAsync()
    {
        if (!_gh.IsAuthenticated) return;
        var repo = GetRepoName();
        var folder = CarpetaBox.Text;
        if (repo == null || string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        { MessageBox.Show("Faltan datos o carpeta.", "Atencion", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        string owner, name;
        if (repo.Contains('/')) { var p = repo.Split('/', 2); owner = p[0]; name = p[1]; }
        else { owner = _user!.Login; name = repo; }

        var nombre = NombreBox.Text.Trim();
        if (string.IsNullOrEmpty(nombre)) nombre = _user!.Name ?? _user.Login;
        var tipo = (TipoCombo.SelectedItem as string ?? "").Trim();
        var msg = string.IsNullOrEmpty(tipo) ? $"Entrega de evaluacion - {nombre}" : $"Entrega de evaluacion - {nombre} ({tipo})";

        Status($"Subiendo a {repo}...");
        Log($"-> Subiendo {folder} a {owner}/{name}");
        var git = new GitService(_gh.Token!, nombre, _user!.Email ?? "");
        var res = await Task.Run(() => git.CommitAndPush(folder, owner, name, msg));
        if (!res.Ok) { Log($"Fallo push: {res.Error}"); Status("Error en push."); return; }

        Log($"OK Subida completada: {res.Url}");
        try { Clipboard.SetText(res.Url!); } catch { }
        await _sb.ReportStudentActivityAsync("upload", _user!.Login, _user.Email, Environment.MachineName, StudentSection.Get(), repo, res.Url);

        var tipoLabel = tipo switch
        {
            "Evaluacion-1" => "Evaluacion Parcial 1", "Evaluacion-2" => "Evaluacion Parcial 2",
            "Evaluacion-3" => "Evaluacion Parcial 3", "Evaluacion-4" => "Evaluacion Parcial 4",
            "Examen" => "Examen Final", _ => "la evaluacion correspondiente"
        };
        MessageBox.Show($"Entrega subida correctamente.\n\nURL (copiada al portapapeles):\n{res.Url}\n\nProximo paso:\n1. Abre el AVA\n2. Ve a {tipoLabel}\n3. Pega el enlace (Ctrl+V)\n4. Envia", "Listo - Entrega en el AVA", MessageBoxButton.OK, MessageBoxImage.Information);

        var del = MessageBox.Show($"Ya terminaste la evaluacion?\n\nSi presionas SI se elimina la carpeta local:\n{folder}\n\n(El repo en GitHub se mantiene)", "Eliminar carpeta local?", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (del == MessageBoxResult.Yes)
        {
            try { Directory.Delete(folder, true); CarpetaBox.Text = ""; Log("Carpeta local eliminada."); UpdateButtonStates(); } catch { }
        }
    }

    // ===================== Classroom assignments =====================
    private List<Assignment> FilterBySection(List<Assignment> all)
    {
        var sec = StudentSection.Get().Trim().ToUpperInvariant();
        return all.Where(a =>
        {
            var s = (a.Section ?? "").Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(s) || (!string.IsNullOrEmpty(sec) && s == sec);
        }).ToList();
    }

    private async Task UpdateAssignmentsBanner()
    {
        var asg = FilterBySection(await _sb.GetActiveAssignmentsAsync());
        if (asg.Count > 0)
        {
            AssignmentsLink.Content = $"Tienes {asg.Count} tarea(s) de Classroom - Click para aceptar";
            AssignmentsLink.Visibility = Visibility.Visible;
        }
        else AssignmentsLink.Visibility = Visibility.Collapsed;
    }

    private async Task ShowAssignmentsDialog()
    {
        var asg = FilterBySection(await _sb.GetActiveAssignmentsAsync());
        if (asg.Count == 0) { MessageBox.Show("No hay tareas activas.", "Sin tareas", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var dlg = new AssignmentsWindow(asg, OpenUrl) { Owner = this };
        dlg.ShowDialog();
    }

    // ===================== Admin polling =====================
    private async Task AdminTickAsync()
    {
        await CheckAdminConfigAsync();
        await UpdateAssignmentsBanner();
        await SendHeartbeatAsync();
        await CheckTargetedLockdownAsync();
    }

    private async Task CheckAdminConfigAsync()
    {
        var cfg = await _sb.GetControlAsync();
        if (cfg == null) return;

        if (cfg.InternetBlock && !_internetBlocked) { Log("[ADMIN] Bloqueo de internet activado."); InternetBlockService.Block(); _internetBlocked = true; }
        else if (!cfg.InternetBlock && _internetBlocked) { Log("[ADMIN] Bloqueo de internet desactivado."); InternetBlockService.Unblock(); _internetBlocked = false; }

        if (cfg.ForceLockdown && !_remoteLockdownActive)
        {
            _remoteLockdownActive = true;
            Log("[ADMIN] Lockdown remoto activado.");
            var alert = new CheatWindow("(remoto)", 0, new[] { "Lockdown remoto del profesor" }, remoteSource: true, checkStillLocked: () => Task.Run(() => _sb.IsForceLockdownAsync()).GetAwaiter().GetResult());
            alert.ShowDialog();
            _remoteLockdownActive = false;
        }

        if (!string.IsNullOrEmpty(cfg.Message) && cfg.Message != _lastAdminMessage)
        {
            _lastAdminMessage = cfg.Message;
            MessageBox.Show(cfg.Message, "Mensaje del profesor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        if (string.IsNullOrEmpty(cfg.Message)) _lastAdminMessage = "";
    }

    private async Task CheckTargetedLockdownAsync()
    {
        if (_targetedLockdownActive || _user == null) return;
        var locked = await _sb.IsTargetedLockedAsync(Environment.MachineName, _user.Login);
        if (locked)
        {
            _targetedLockdownActive = true;
            var reason = await _sb.GetTargetedReasonAsync(Environment.MachineName, _user.Login) ?? "El profesor te bloqueo";
            Log("[ADMIN] Lockdown DIRIGIDO a tu PC.");
            var me = _user.Login;
            var alert = new CheatWindow("(dirigido)", 0, new[] { reason }, remoteSource: true,
                checkStillLocked: () => Task.Run(() =>
                    _sb.IsTargetedLockedAsync(Environment.MachineName, me).GetAwaiter().GetResult()
                    || _sb.IsForceLockdownAsync().GetAwaiter().GetResult()).GetAwaiter().GetResult());
            alert.ShowDialog();
            _targetedLockdownActive = false;
        }
    }

    private async Task SendHeartbeatAsync()
    {
        if (_user == null) return;
        // Tipo totalmente calificado: WPF-UI introduce otro ProcessInfo via
        // sus global usings, asi que apuntamos al DTO propio sin ambiguedad.
        List<EntregaEvaluacion.Models.ProcessInfo> procs = ProcessMonitor.GetOpenWindows();

        // Detectar nuevos sospechosos
        var current = new HashSet<string>();
        foreach (EntregaEvaluacion.Models.ProcessInfo p in procs)
        {
            var key = $"{p.Name}:{p.Pid}";
            current.Add(key);
            if (!_lastProcSet.Contains(key) && ProcessMonitor.IsSuspicious(p.Name))
                await _sb.ReportProcessAlertAsync(_user.Login, Environment.MachineName, StudentSection.Get(), p.Name, p.Title);
        }
        _lastProcSet.Clear();
        foreach (var k in current) _lastProcSet.Add(k);

        var internetState = InternetBlockService.IsBlocked() ? "blocked" : "free";
        var lockdownState = (_remoteLockdownActive || _targetedLockdownActive) ? "active" : "none";
        await _sb.SendHeartbeatAsync(Environment.MachineName, _user.Login, _user.Email,
            StudentSection.Get(), procs, internetState, lockdownState);
    }

    // ===================== Helpers =====================
    private void Log(string msg)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Log(msg)); return; }
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        LogBox.CaretIndex = LogBox.Text.Length;
        LogBox.ScrollToEnd();
    }

    private void Status(string msg)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Status(msg)); return; }
        StatusText.Text = msg;
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
    }

    private void OpenFolder(string folder)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = folder, UseShellExecute = true }); }
        catch { }
    }

    private void OpenPythonIdle(string folder)
    {
        foreach (var py in new[] { "pythonw", "python", "py" })
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = py, Arguments = "-m idlelib", WorkingDirectory = folder, UseShellExecute = false });
                Log($"OK IDLE abierto en: {folder}");
                return;
            }
            catch { }
        }
        Log("Python no encontrado. Abre IDLE manualmente.");
    }
}
