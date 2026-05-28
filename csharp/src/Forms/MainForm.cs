using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using EntregaEvaluacion.Models;
using EntregaEvaluacion.Services;

namespace EntregaEvaluacion.Forms;

public class MainForm : Form
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

    // Controles
    private ComboBox _cmbSection = null!;
    private LinkLabel _lnkAssignments = null!;
    private Label _lblSesionUser = null!;
    private Label _lblSesionEmail = null!;
    private Button _btnLogin = null!;
    private Button _btnLogout = null!;
    private RadioButton _rbNuevo = null!;
    private RadioButton _rbExistente = null!;
    private TextBox _txtNombre = null!;
    private ComboBox _cmbTipo = null!;
    private ComboBox _cmbRepos = null!;
    private Button _btnRefresh = null!;
    private TextBox _txtCarpeta = null!;
    private Button _btnBuscar = null!;
    private Label _lblRepoDestino = null!;
    private Button _btnCrear = null!;
    private Button _btnSubir = null!;
    private TextBox _txtLog = null!;
    private Label _lblStatus = null!;
    private System.Windows.Forms.Timer _adminTimer = null!;

    public MainForm()
    {
        BuildUi();
        Load += async (_, _) => await InitAsync();
    }

    // ===================== UI =====================
    private void BuildUi()
    {
        Text = "Subir Evaluacion a GitHub";
        Size = new Size(640, 705);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var lblTitulo = new Label
        {
            Text = "Evaluacion -> GitHub",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Location = new Point(20, 15), Size = new Size(420, 35)
        };
        Controls.Add(lblTitulo);

        // Seccion
        Controls.Add(new Label { Text = "Tu seccion:", Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(20, 58), Size = new Size(80, 20) });
        _cmbSection = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(105, 56), Size = new Size(90, 22) };
        _cmbSection.Items.AddRange(Config.Sections);
        _cmbSection.SelectedIndexChanged += (_, _) => { StudentSection.Set((string)_cmbSection.SelectedItem!); _ = UpdateAssignmentsBanner(); };
        Controls.Add(_cmbSection);

        _lnkAssignments = new LinkLabel { Text = "", Visible = false, Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(210, 58), Size = new Size(230, 22) };
        _lnkAssignments.LinkClicked += async (_, _) => await ShowAssignmentsDialog();
        Controls.Add(_lnkAssignments);

        // Panel sesion
        var grpSesion = new GroupBox { Text = "Cuenta de GitHub", Location = new Point(440, 5), Size = new Size(180, 50) };
        _lblSesionUser = new Label { Text = "Verificando...", Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(8, 18), Size = new Size(165, 14), ForeColor = Color.Gray };
        _lblSesionEmail = new Label { Text = "", Font = new Font("Segoe UI", 7), Location = new Point(8, 32), Size = new Size(165, 14), ForeColor = Color.DimGray };
        grpSesion.Controls.Add(_lblSesionUser);
        grpSesion.Controls.Add(_lblSesionEmail);
        Controls.Add(grpSesion);

        _btnLogin = new Button { Text = "Iniciar sesion", Location = new Point(440, 58), Size = new Size(85, 28), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8) };
        _btnLogin.Click += async (_, _) => await DoLoginAsync();
        Controls.Add(_btnLogin);

        _btnLogout = new Button { Text = "Cerrar sesion", Location = new Point(530, 58), Size = new Size(90, 28), BackColor = Color.FromArgb(198, 40, 40), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8), Enabled = false };
        _btnLogout.Click += async (_, _) => await DoLogoutAsync();
        Controls.Add(_btnLogout);

        var lnkCrear = new LinkLabel { Text = "No tienes cuenta? Creala aqui", Font = new Font("Segoe UI", 8, FontStyle.Underline), Location = new Point(440, 90), Size = new Size(180, 18), TextAlign = ContentAlignment.MiddleRight };
        lnkCrear.LinkClicked += (_, _) => OpenUrl("https://github.com/signup");
        Controls.Add(lnkCrear);

        // Modo de subida
        var grpModo = new GroupBox { Text = "Modo de subida", Location = new Point(20, 95), Size = new Size(600, 55) };
        _rbNuevo = new RadioButton { Text = "Crear repositorio nuevo", Location = new Point(15, 22), Size = new Size(220, 22), Checked = true };
        _rbExistente = new RadioButton { Text = "Usar repositorio existente de mi cuenta", Location = new Point(280, 22), Size = new Size(300, 22) };
        _rbNuevo.CheckedChanged += (_, _) => { if (_rbNuevo.Checked) SetModoUi(); };
        _rbExistente.CheckedChanged += async (_, _) => { if (_rbExistente.Checked) { SetModoUi(); await LoadUserReposAsync(); } };
        grpModo.Controls.Add(_rbNuevo);
        grpModo.Controls.Add(_rbExistente);
        Controls.Add(grpModo);

        // Nombre
        Controls.Add(new Label { Text = "Nombre completo:", Location = new Point(20, 165), Size = new Size(150, 20) });
        _txtNombre = new TextBox { Location = new Point(180, 163), Size = new Size(420, 22) };
        _txtNombre.TextChanged += (_, _) => { UpdateRepoPreview(); UpdateButtonStates(); };
        Controls.Add(_txtNombre);

        // Tipo eval
        Controls.Add(new Label { Text = "Tipo de evaluacion:", Location = new Point(20, 200), Size = new Size(150, 20) });
        _cmbTipo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(180, 198), Size = new Size(420, 22) };
        _cmbTipo.Items.AddRange(Config.EvaluationTypes);
        _cmbTipo.SelectedIndexChanged += (_, _) => { UpdateRepoPreview(); UpdateButtonStates(); };
        Controls.Add(_cmbTipo);

        // Repo existente
        Controls.Add(new Label { Text = "Tu repositorio:", Location = new Point(20, 235), Size = new Size(150, 20) });
        _cmbRepos = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(180, 233), Size = new Size(330, 22), Enabled = false };
        _cmbRepos.SelectedIndexChanged += (_, _) => { UpdateRepoPreview(); UpdateButtonStates(); };
        Controls.Add(_cmbRepos);
        _btnRefresh = new Button { Text = "Refrescar", Location = new Point(520, 232), Size = new Size(80, 25), Enabled = false };
        _btnRefresh.Click += async (_, _) => await LoadUserReposAsync();
        Controls.Add(_btnRefresh);

        // Carpeta
        Controls.Add(new Label { Text = "Carpeta del proyecto:", Location = new Point(20, 270), Size = new Size(150, 20) });
        _txtCarpeta = new TextBox { Location = new Point(180, 268), Size = new Size(330, 22), ReadOnly = true };
        Controls.Add(_txtCarpeta);
        _btnBuscar = new Button { Text = "Buscar...", Location = new Point(520, 267), Size = new Size(80, 25) };
        _btnBuscar.Click += (_, _) => BuscarCarpeta();
        Controls.Add(_btnBuscar);

        // Preview
        Controls.Add(new Label { Text = "Repo destino:", Location = new Point(20, 305), Size = new Size(150, 20) });
        _lblRepoDestino = new Label { Text = "(rellenar nombre y tipo)", Font = new Font("Consolas", 10), ForeColor = Color.Gray, Location = new Point(180, 305), Size = new Size(420, 20) };
        Controls.Add(_lblRepoDestino);

        Controls.Add(new Label { Text = "Los repositorios se crean publicos para que el profesor pueda verlos.", Font = new Font("Segoe UI", 8, FontStyle.Italic), ForeColor = Color.FromArgb(96, 96, 96), Location = new Point(20, 335), Size = new Size(600, 20) });

        // Botones
        _btnCrear = new Button { Text = "1. Crear Repo", Location = new Point(30, 365), Size = new Size(280, 35), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        _btnCrear.Click += async (_, _) => await CrearRepoAsync();
        Controls.Add(_btnCrear);
        _btnSubir = new Button { Text = "2. Subir Archivos", Location = new Point(330, 365), Size = new Size(280, 35), BackColor = Color.FromArgb(76, 175, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        _btnSubir.Click += async (_, _) => await SubirArchivosAsync();
        Controls.Add(_btnSubir);

        // Log
        Controls.Add(new Label { Text = "Salida:", Location = new Point(20, 410), Size = new Size(100, 20) });
        _txtLog = new TextBox { Location = new Point(20, 435), Size = new Size(600, 195), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LimeGreen, Font = new Font("Consolas", 9) };
        Controls.Add(_txtLog);

        _lblStatus = new Label { Text = "Listo.", Location = new Point(20, 640), Size = new Size(600, 20) };
        Controls.Add(_lblStatus);
    }

    // ===================== Init =====================
    private async Task InitAsync()
    {
        Log("Listo. Completa los datos y elige una accion.");

        // Seccion guardada o pedir
        var saved = StudentSection.Get();
        if (!string.IsNullOrEmpty(saved) && Config.Sections.Contains(saved))
            _cmbSection.SelectedItem = saved;
        else
            PromptSection();

        await UpdateSessionPanel();
        SetModoUi();
        UpdateButtonStates();
        await UpdateAssignmentsBanner();

        // Timer admin
        _adminTimer = new System.Windows.Forms.Timer { Interval = Config.PollIntervalMs };
        _adminTimer.Tick += async (_, _) => await AdminTickAsync();
        _adminTimer.Start();
        await AdminTickAsync();
    }

    private void PromptSection()
    {
        using var dlg = new Form { Text = "Selecciona tu seccion", Size = new Size(340, 200), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, ControlBox = false };
        dlg.Controls.Add(new Label { Text = "Elige tu seccion:", Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(20, 20), Size = new Size(280, 22) });
        var cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(20, 55), Size = new Size(280, 25), Font = new Font("Segoe UI", 11) };
        cmb.Items.AddRange(Config.Sections);
        cmb.SelectedIndex = 0;
        dlg.Controls.Add(cmb);
        var btn = new Button { Text = "Continuar", Location = new Point(110, 120), Size = new Size(110, 32), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
        dlg.Controls.Add(btn);
        dlg.AcceptButton = btn;
        dlg.ShowDialog();
        var sel = (string)cmb.SelectedItem!;
        _cmbSection.SelectedItem = sel;
        StudentSection.Set(sel);
    }

    // ===================== Sesion =====================
    private async Task UpdateSessionPanel()
    {
        if (_gh.IsAuthenticated)
        {
            _user = await _gh.GetUserAsync();
            if (_user != null)
            {
                _lblSesionUser.Text = "@" + _user.Login;
                _lblSesionUser.ForeColor = Color.DarkGreen;
                _lblSesionEmail.Text = _user.Email ?? "(email privado)";
                _btnLogin.Enabled = false;
                _btnLogout.Enabled = true;
            }
        }
        else
        {
            _user = null;
            _lblSesionUser.Text = "Sin sesion";
            _lblSesionUser.ForeColor = Color.Gray;
            _lblSesionEmail.Text = "(no conectado)";
            _btnLogin.Enabled = true;
            _btnLogout.Enabled = false;
        }
        UpdateButtonStates();
    }

    private async Task DoLoginAsync()
    {
        Log("-> Iniciando sesion con codigo...");
        using var dlg = new LoginDialog(_gh);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await UpdateSessionPanel();
            Log("Sesion iniciada.");
        }
    }

    private async Task DoLogoutAsync()
    {
        if (!_gh.IsAuthenticated) return;
        var r = MessageBox.Show("Cerrar sesion y borrar credenciales de este equipo?", "Cerrar sesion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r != DialogResult.Yes) return;
        _gh.Logout();
        Log("Sesion cerrada.");
        await UpdateSessionPanel();
    }

    // ===================== Modo UI =====================
    private void SetModoUi()
    {
        if (_rbExistente.Checked)
        {
            _cmbRepos.Enabled = true; _btnRefresh.Enabled = true;
            _txtNombre.Enabled = false; _cmbTipo.Enabled = false;
            _btnCrear.Text = "1. Clonar Repo";
        }
        else
        {
            _cmbRepos.Enabled = false; _btnRefresh.Enabled = false;
            _txtNombre.Enabled = true; _cmbTipo.Enabled = true;
            _btnCrear.Text = "1. Crear Repo";
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
        if (_rbExistente.Checked)
        {
            if (_cmbRepos.SelectedItem == null) return null;
            return Regex.Replace((string)_cmbRepos.SelectedItem, @"^\S+\s+", "").Trim();
        }
        var n = _txtNombre.Text.Trim();
        var t = _cmbTipo.Text.Trim();
        if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(t)) return null;
        return Sanitize($"{n}-{t}");
    }

    private void UpdateRepoPreview()
    {
        var repo = GetRepoName();
        if (repo != null) { _lblRepoDestino.Text = repo; _lblRepoDestino.ForeColor = Color.Black; }
        else { _lblRepoDestino.Text = _rbExistente.Checked ? "(selecciona un repositorio)" : "(rellenar nombre y tipo)"; _lblRepoDestino.ForeColor = Color.Gray; }
    }

    private void UpdateButtonStates()
    {
        var hasAuth = _gh.IsAuthenticated;
        var hasFolder = !string.IsNullOrEmpty(_txtCarpeta.Text) && Directory.Exists(_txtCarpeta.Text);
        var hasRepoData = _rbExistente.Checked ? _cmbRepos.SelectedItem != null
            : (!string.IsNullOrEmpty(_txtNombre.Text.Trim()) && !string.IsNullOrEmpty(_cmbTipo.Text.Trim()));
        _btnCrear.Enabled = hasAuth && hasRepoData;
        _btnSubir.Enabled = hasAuth && hasRepoData && hasFolder;
    }

    private void BuscarCarpeta()
    {
        using var fbd = new FolderBrowserDialog { Description = "Selecciona la carpeta con tu evaluacion" };
        if (fbd.ShowDialog() == DialogResult.OK)
        {
            _txtCarpeta.Text = fbd.SelectedPath;
            Log($"Carpeta: {fbd.SelectedPath}");
            UpdateButtonStates();
        }
    }

    // ===================== Repos =====================
    private async Task LoadUserReposAsync()
    {
        if (!_gh.IsAuthenticated) { Log("Inicia sesion primero."); return; }
        Log("-> Cargando repos (incluye Classroom)...");
        _cmbRepos.Items.Clear();
        var repos = await _gh.ListReposAsync();
        var me = _user?.Login ?? "";
        var sorted = repos.Where(r => !r.Archived)
            .OrderBy(r => r.Owner?.Login == me ? 1 : 0).ThenBy(r => r.FullName);
        int count = 0;
        foreach (var r in sorted)
        {
            var vis = r.Private ? "[Priv]" : "[Pub]";
            var disp = r.Owner?.Login != me ? $"{vis} {r.FullName}" : $"{vis} {r.Name}";
            _cmbRepos.Items.Add(disp);
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
        var r = MessageBox.Show($"Tienes {invites.Count} invitacion(es):\n\n{list}\n\nAceptar todas?", "Invitaciones pendientes", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (r != DialogResult.Yes) return false;
        int ok = 0;
        var urls = new List<string>();
        foreach (var inv in invites)
        {
            if (await _gh.AcceptInvitationAsync(inv.Id)) { ok++; if (inv.Repository != null) urls.Add($"https://github.com/{inv.Repository.FullName}"); }
        }
        if (ok > 0)
        {
            try { Clipboard.SetText(string.Join("\n", urls)); } catch { }
            MessageBox.Show($"Se aceptaron {ok} invitacion(es).\n\nURL copiada al portapapeles:\n{string.Join("\n", urls)}\n\nPega el enlace en la evaluacion correspondiente del AVA.", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        return false;
    }

    // ===================== Crear / Clonar =====================
    private async Task CrearRepoAsync()
    {
        if (!_gh.IsAuthenticated) { Log("Inicia sesion primero."); return; }
        var repo = GetRepoName();
        if (repo == null) { MessageBox.Show("Faltan datos.", "Atencion", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        if (_rbExistente.Checked)
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
            if (!created) { Log("Error creando repo."); MessageBox.Show("No se pudo crear el repo.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            Log("OK Repo creado.");
        }

        var url = $"https://github.com/{_user!.Login}/{repo}";
        var folder = _txtCarpeta.Text;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) OpenFolder(folder);
        MessageBox.Show($"Repositorio creado correctamente.\n\nURL: {url}\n\nProximo paso: 'Subir Archivos'.", "Repositorio creado", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        if (rep == null) { Log("No se pudo acceder al repo."); MessageBox.Show("No se encontro el repo. Refresca o crea uno nuevo.", "Atencion", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

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
                _txtCarpeta.Text = "";
                LockdownService.Trigger(repo, clean.FilesCount, clean.FilesNames);
                await _sb.ReportCheatEventAsync(_user!.Login, Environment.MachineName, repo, clean.FilesCount, clean.FilesNames);
                using var alert = new CheatAlertForm(repo, clean.FilesCount, clean.FilesNames);
                alert.ShowDialog();
                UpdateButtonStates();
                return;
            }
            Log("OK Repo limpio.");
        }

        _txtCarpeta.Text = target;
        OpenPythonIdle(target);
        await _sb.ReportStudentActivityAsync("clone", _user!.Login, _user.Email, Environment.MachineName, StudentSection.Get(), repo, $"https://github.com/{owner}/{name}");
        MessageBox.Show($"Repo clonado en:\n{target}\n\nSe abrio IDLE de Python.\n\nEdita, guarda (Ctrl+S), y luego 'Subir Archivos'.", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Status("Edita en IDLE y luego Subir Archivos.");
        UpdateButtonStates();
    }

    // ===================== Subir =====================
    private async Task SubirArchivosAsync()
    {
        if (!_gh.IsAuthenticated) return;
        var repo = GetRepoName();
        var folder = _txtCarpeta.Text;
        if (repo == null || string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        { MessageBox.Show("Faltan datos o carpeta.", "Atencion", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        string owner, name;
        if (repo.Contains('/')) { var p = repo.Split('/', 2); owner = p[0]; name = p[1]; }
        else { owner = _user!.Login; name = repo; }

        var nombre = _txtNombre.Text.Trim();
        if (string.IsNullOrEmpty(nombre)) nombre = _user!.Name ?? _user.Login;
        var tipo = _cmbTipo.Text.Trim();
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
        MessageBox.Show($"Entrega subida correctamente.\n\nURL (copiada al portapapeles):\n{res.Url}\n\nProximo paso:\n1. Abre el AVA\n2. Ve a {tipoLabel}\n3. Pega el enlace (Ctrl+V)\n4. Envia", "Listo - Entrega en el AVA", MessageBoxButtons.OK, MessageBoxIcon.Information);

        var del = MessageBox.Show($"Ya terminaste la evaluacion?\n\nSi presionas SI se elimina la carpeta local:\n{folder}\n\n(El repo en GitHub se mantiene)", "Eliminar carpeta local?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (del == DialogResult.Yes)
        {
            try { Directory.Delete(folder, true); _txtCarpeta.Text = ""; Log("Carpeta local eliminada."); UpdateButtonStates(); } catch { }
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
            _lnkAssignments.Text = $"Tienes {asg.Count} tarea(s) de Classroom - Click para aceptar";
            _lnkAssignments.Visible = true;
        }
        else _lnkAssignments.Visible = false;
    }

    private async Task ShowAssignmentsDialog()
    {
        var asg = FilterBySection(await _sb.GetActiveAssignmentsAsync());
        if (asg.Count == 0) { MessageBox.Show("No hay tareas activas.", "Sin tareas", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        using var dlg = new Form { Text = "Tareas de GitHub Classroom", Size = new Size(560, 420), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false };
        dlg.Controls.Add(new Label { Text = "Tareas asignadas por el profesor", Font = new Font("Segoe UI", 12, FontStyle.Bold), Location = new Point(20, 15), Size = new Size(500, 25) });
        dlg.Controls.Add(new Label { Text = "Click 'Aceptar' -> se abre Classroom -> acepta en GitHub -> vuelve y usa 'Repositorio existente'.", Font = new Font("Segoe UI", 9), ForeColor = Color.DimGray, Location = new Point(20, 45), Size = new Size(500, 40) });

        int y = 100;
        foreach (var a in asg)
        {
            dlg.Controls.Add(new Label { Text = a.Title, Font = new Font("Segoe UI", 11, FontStyle.Bold), Location = new Point(20, y), Size = new Size(340, 22) });
            var url = a.ClassroomUrl;
            var btn = new Button { Text = "Aceptar tarea", Location = new Point(370, y - 2), Size = new Size(150, 28), BackColor = Color.FromArgb(76, 175, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btn.Click += (_, _) => OpenUrl(url);
            dlg.Controls.Add(btn);
            y += 50;
        }
        var btnClose = new Button { Text = "Cerrar", Location = new Point(220, 350), Size = new Size(100, 32), DialogResult = DialogResult.OK };
        dlg.Controls.Add(btnClose);
        dlg.AcceptButton = btnClose;
        dlg.ShowDialog(this);
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
            using var alert = new CheatAlertForm("(remoto)", 0, new[] { "Lockdown remoto del profesor" }, remoteSource: true, checkStillLocked: () => Task.Run(() => _sb.IsForceLockdownAsync()).GetAwaiter().GetResult());
            alert.ShowDialog();
            _remoteLockdownActive = false;
        }

        if (!string.IsNullOrEmpty(cfg.Message) && cfg.Message != _lastAdminMessage)
        {
            _lastAdminMessage = cfg.Message;
            MessageBox.Show(cfg.Message, "Mensaje del profesor", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            using var alert = new CheatAlertForm("(dirigido)", 0, new[] { reason }, remoteSource: true,
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
        var procs = ProcessMonitor.GetOpenWindows();

        // Detectar nuevos sospechosos
        var current = new HashSet<string>();
        foreach (var p in procs)
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
        if (InvokeRequired) { Invoke(() => Log(msg)); return; }
        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        _txtLog.SelectionStart = _txtLog.Text.Length;
        _txtLog.ScrollToCaret();
    }

    private void Status(string msg)
    {
        if (InvokeRequired) { Invoke(() => Status(msg)); return; }
        _lblStatus.Text = msg;
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
