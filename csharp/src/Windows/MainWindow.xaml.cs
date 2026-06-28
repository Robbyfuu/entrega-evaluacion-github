using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using EntregaEvaluacion.Core;
using EntregaEvaluacion.Models;
using EntregaEvaluacion.Services;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Ventana principal (equivalente a MainForm). Conserva EXACTA la logica de
/// negocio: sesion, modo crear/existente, crear/clonar repo, subir, banner de
/// Classroom, y polling admin (config, heartbeat, lockdown dirigido/remoto).
/// Solo cambia la capa UI (WPF + WPF-UI, layout fluido en lugar de coords).
/// </summary>
public partial class MainWindow : Window, ILogSink, IUserNotifier
{
    private readonly IGitHubService _gh;
    private readonly ISupabaseClient _sb;

    // Seleccion del alumno (seccion + evaluacion) detras de ISelectionStore, en
    // reemplazo de la clase estatica global StudentSection. Se inyecta por el
    // constructor desde el composition root (App.StartShell), igual que _gh/_sb
    // (ENT-6 step 5).
    private readonly ISelectionStore _selection;

    // Estado
    private GitHubUser? _user;
    private bool _internetBlocked;
    private bool _copilotBlocked;
    private bool _remoteLockdownActive;
    private bool _targetedLockdownActive;
    private string _lastAdminMessage = "";
    private readonly HashSet<string> _lastProcSet = new();

    // Blocklist efectivo (global union seccion) cacheado desde la tabla
    // suspicious_processes. Se refresca en cada AdminTick. null = fallback a
    // Config.SuspiciousProcesses (fetch fallido o sin datos validos).
    private IReadOnlySet<string>? _blocklist;

    // Multi-evaluacion: cursos, secciones y evaluaciones fetcheados de BD.
    // null/empty = fallback a Config.cs (mismo patron que _blocklist).
    private List<Course> _courses = new();
    private List<SectionRow> _sections = new();
    private List<Evaluation> _currentEvaluations = new();

    // Roster: confirmacion de matricula del alumno actual contra enrollments
    // (via RPC get_my_enrollment, no-PII). null = todavia no consultado.
    // Confirmed=false => no se pudo confirmar (NO equivale a "no matriculado").
    // Es ADITIVO: solo endurece EXPECTED cuando hay match; en no-match o
    // no-confirmado se cae al comportamiento por defecto, sin hard-block y sin
    // suprimir entregas pendientes (la verdad de entrega es por github_username).
    private MyEnrollment? _enrollment;

    private DispatcherTimer _adminTimer = null!;

    // Evita disparar handlers durante la carga inicial de combos.
    private bool _initializing = true;

    // Evita que SyncTipoCombo reintre en TipoCombo_SelectionChanged al espejar
    // la evaluacion seleccionada en el sidebar hacia el TipoCombo (read-only).
    private bool _syncingTipo;

    // ===== UI: log en memoria, toast, accion primaria =====
    private readonly System.Text.StringBuilder _logBuffer = new();
    private DispatcherTimer? _toastTimer;
    private LogDetailWindow? _logWindow;

    // El handler que el boton primario debe disparar segun el estado actual.
    private Func<Task>? _primaryAction;

    public MainWindow(IGitHubService gh, ISupabaseClient sb, ISelectionStore selection)
    {
        // Las dependencias se asignan ANTES de InitializeComponent y de cualquier
        // otro codigo del cuerpo del constructor que pudiera usarlas: en C# los
        // inicializadores de campo ya corrieron, asi que estos campos solo quedan
        // definidos una vez que el composition root los entrega aqui.
        _gh = gh;
        _sb = sb;
        _selection = selection;

        InitializeComponent();

        // Los combos se poblan asincronicamente en InitAsync (fetch BD + fallback
        // a Config.cs), igual que _blocklist. No poblamos aca para evitar datos
        // legacy antes de saber si la BD responde.
        Loaded += async (_, _) => await InitAsync();
    }

    // El programa NO se cierra durante la evaluacion: el alumno no puede salir
    // del control (y el daemon lo relanzaria igual). Solo se cierra con la clave
    // del profesor. _allowExit pasa a true cuando la clave es correcta.
    private bool _allowExit;
    private DateTime _lastCloseReport = DateTime.MinValue;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        // Permitir el cierre si: ya se autorizo con clave, otro handler lo cancelo,
        // o se esta aplicando un update (Velopack reinicia la app).
        if (_allowExit || e.Cancel || UpdateService.IsApplying) return;

        // Bloquear el cierre + avisar + registrar el intento.
        e.Cancel = true;
        ShowToast("No puedes cerrar el programa durante la evaluacion. Intento registrado.", ToastKind.Error);
        _ = ReportCloseAttemptAsync();

        // Escape del profesor: clave correcta => cerrar de verdad (y sacar el
        // daemon para que no lo relance).
        var dlg = new PasswordPromptWindow { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _allowExit = true;
            try { DaemonService.Unregister(); } catch { }
            try { ExamPdfService.DeleteAllDownloaded(); } catch { }
            Application.Current.Shutdown();
        }
    }

    /// <summary>
    /// Reporta el intento de cierre al panel (queda en Actividad). Throttle de
    /// 30s para no spamear si el alumno aprieta la X varias veces.
    /// </summary>
    private async Task ReportCloseAttemptAsync()
    {
        if (_user == null) return;
        if ((DateTime.UtcNow - _lastCloseReport).TotalSeconds < 30) return;
        _lastCloseReport = DateTime.UtcNow;
        try
        {
            await _sb.ReportStudentActivityAsync(
                "close_attempt", _user.Login, _user.Email, Environment.MachineName,
                _selection.SectionText, "", null, _selection.SectionId);
        }
        catch { }
    }

    // ===================== Init =====================
    private async Task InitAsync()
    {
        Log("Listo. Completa los datos y elige una accion.");
        UpdateThemeButton();

        // Limpiar cualquier enunciado PDF que haya quedado de una sesion previa.
        try { ExamPdfService.CleanupPendingOnStartup(); } catch { }

        // NOTA: el chequeo de update NO es automatico (evita rafagas a la API de
        // GitHub = rate limit cuando muchos alumnos abren a la vez). La
        // actualizacion es MANUAL: el alumno aprieta "Actualizar version" (boton
        // del sidebar) o el profe la dispara desde el panel (control.update_requested_at,
        // que se chequea en AdminTickAsync). Mostrar la version actual al arrancar.
        VersionText.Text = $"v{UpdateService.CurrentVersion()}";

        _initializing = true;

        // Multi-evaluacion: fetch cursos y secciones (todas; se filtran por
        // curso al seleccionar). Fallback a Config.cs si la BD no responde
        // (mismo patron que _blocklist / SuspiciousProcesses).
        _courses = await _sb.GetCoursesAsync();
        _sections = await _sb.GetSectionsAsync();

        // CursoCombo (oculto si no hay cursos: modo fallback legacy)
        CursoCombo.Items.Clear();
        CursoCombo.DisplayMemberPath = "Name";
        foreach (var c in _courses) CursoCombo.Items.Add(c);
        CursoCombo.Visibility = _courses.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Resolver seccion guardada contra las secciones fetcheadas.
        // Priorizar section_id (identidad real) sobre section TEXT (codigo,
        // que puede repetirse entre cursos). Si section_id no existe, caer
        // a buscar por code como fallback legacy.
        var savedCode = _selection.SectionText;
        var savedSectionId = _selection.SectionId;
        var savedEvalId = _selection.EvaluationId;
        var savedRow = savedSectionId.HasValue
            ? _sections.FirstOrDefault(s => s.Id == savedSectionId.Value)
            : null;
        savedRow ??= _sections.FirstOrDefault(s => s.Code == savedCode);

        if (savedRow != null)
        {
            // Seleccionar curso padre y poblar solo sus secciones
            SelectCourseById(savedRow.CourseId);
            PopulateSectionCombo(savedRow.CourseId);
            SectionCombo.SelectedItem = savedRow.Code;
            _selection.SetSectionText(savedRow.Code);
            _selection.SetSectionId(savedRow.Id);
            await LoadEvaluationsForSection(savedRow.Code, savedRow.Id);
            RestoreEvaluationSelection(savedEvalId);
        }
        else
        {
            // No hay seccion guardada o ya no existe en BD: poblar todas y pedir
            PopulateSectionCombo(null);
            PromptSection();
            var sel = (string?)SectionCombo.SelectedItem;
            if (!string.IsNullOrEmpty(sel))
            {
                var row = _sections.FirstOrDefault(s => s.Code == sel);
                _selection.SetSectionId(row?.Id);
                await LoadEvaluationsForSection(sel, row?.Id);
            }
        }

        _initializing = false;

        await UpdateSessionPanel();
        // El cliente Supabase puede re-enrolar la identidad por su cuenta cuando
        // el JWT esta por expirar, pidiendo el token de GitHub vigente.
        _sb.SetGitHubTokenProvider(() => _gh.Token);
        // Si ya hay sesion guardada de un arranque anterior, enrolar la identidad
        // ahora (best-effort) para no esperar a un nuevo login.
        if (_gh.IsAuthenticated) await EnrollIdentityAsync();
        SetModoUi();
        UpdateButtonStates();
        await UpdateAssignmentsBanner();

        // Timer admin
        _adminTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Config.PollIntervalMs) };
        _adminTimer.Tick += async (_, _) => await AdminTickAsync();
        _adminTimer.Start();
        await AdminTickAsync();
    }

    /// <summary>Selecciona en CursoCombo el curso con el Id dado (no-op si no existe).</summary>
    private void SelectCourseById(long courseId)
    {
        for (int i = 0; i < CursoCombo.Items.Count; i++)
            if (CursoCombo.Items[i] is Course c && c.Id == courseId)
            {
                CursoCombo.SelectedIndex = i;
                return;
            }
    }

    /// <summary>
    /// Pobla SectionCombo con los CODIGOS de seccion (strings, igual que antes
    /// para no romper las lectoras que castea SelectedItem a string). Filtra por
    /// curso si courseId viene dado; si no hay secciones fetcheadas cae a
    /// Config.Sections (fallback legacy).
    /// </summary>
    private void PopulateSectionCombo(long? courseId)
    {
        SectionCombo.Items.Clear();
        if (_sections.Count == 0)
        {
            foreach (var s in Config.Sections) SectionCombo.Items.Add(s);
            return;
        }
        var filtered = courseId is { } cid ? _sections.Where(s => s.CourseId == cid) : _sections;
        foreach (var s in filtered) SectionCombo.Items.Add(s.Code);
    }

    /// <summary>
    /// Pobla EvaluationCombo con las evaluaciones activas de la seccion dada
    /// (fetch BD). Distingue dos casos:
    /// - sectionId != null (BD viva): muestra lo que la BD devuelva, aunque
    ///   sea vacio. Una lista vacia significa "el profe no activo ninguna
    ///   evaluacion para esta seccion" — NO se inventa opciones de fallback
    ///   porque eso confundiria al alumno con evaluaciones que el profe no
    ///   activo (puede dejar el combo vacio legiblemente).
    /// - sectionId == null (sin BD o modo legacy): sintetiza Evaluations
    ///   desde Config.EvaluationTypes (Id=0 = sentinel de fallback).
    /// </summary>
    private async Task LoadEvaluationsForSection(string sectionCode, long? sectionId)
    {
        EvaluationCombo.Items.Clear();
        EvaluationCombo.DisplayMemberPath = "Title";
        _currentEvaluations = sectionId is { } sid ? await _sb.GetEvaluationsAsync(sid) : new();

        if (_currentEvaluations.Count > 0)
        {
            foreach (var ev in _currentEvaluations) EvaluationCombo.Items.Add(ev);
        }
        else if (sectionId == null)
        {
            // Modo legacy: no hay section_id real, cae a Config.EvaluationTypes.
            foreach (var t in Config.EvaluationTypes)
                EvaluationCombo.Items.Add(new Evaluation { Id = 0, Title = t, Active = true });
        }
        // Si sectionId != null pero _currentEvaluations esta vacio, el combo
        // queda vacio (el profe no activo evaluaciones para esta seccion).
    }

    /// <summary>Restaura la evaluacion guardada (por Id) y espeja su titulo en TipoCombo.</summary>
    private void RestoreEvaluationSelection(long? evalId)
    {
        if (!evalId.HasValue || evalId.Value <= 0) return;
        for (int i = 0; i < EvaluationCombo.Items.Count; i++)
            if (EvaluationCombo.Items[i] is Evaluation ev && ev.Id == evalId.Value)
            {
                EvaluationCombo.SelectedIndex = i;
                SyncTipoCombo(ev.Title);
                return;
            }
    }

    /// <summary>
    /// Espeja el titulo de la evaluacion seleccionada en el sidebar hacia
    /// TipoCombo (read-only). Asi GetRepoName/SubirArchivosAsync siguen leyendo
    /// TipoCombo.SelectedItem como string sin cambios.
    /// </summary>
    private void SyncTipoCombo(string title)
    {
        _syncingTipo = true;
        TipoCombo.Items.Clear();
        TipoCombo.Items.Add(title);
        TipoCombo.SelectedIndex = 0;
        _syncingTipo = false;
        UpdateRepoPreview();
        UpdateButtonStates();
    }

    private void PromptSection()
    {
        var dlg = new SectionPromptWindow { Owner = this };
        // Si hay secciones fetcheadas, ofrecerlas; si no, el dialogo usa
        // Config.Sections (su constructor las carga por defecto).
        if (_sections.Count > 0)
        {
            dlg.SectionCombo.Items.Clear();
            foreach (var s in _sections) dlg.SectionCombo.Items.Add(s.Code);
            dlg.SectionCombo.SelectedIndex = 0;
        }
        dlg.ShowDialog();
        var sel = dlg.SelectedSection;
        SectionCombo.SelectedItem = sel;
        _selection.SetSectionText(sel);
    }

    // ===================== Eventos UI =====================
    private void CursoCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing || CursoCombo.SelectedItem == null) return;
        var courseId = ((Course)CursoCombo.SelectedItem).Id;
        PopulateSectionCombo(courseId);
        // Reset de la cascada abajo: seccion, evaluacion y TipoCombo.
        // Limpiar TAMBIEN section TEXT (no solo section_id) para que
        // heartbeat/blocklist no manden una seccion vieja mezclada con
        // curso nuevo durante la ventana hasta que el alumno elija.
        SectionCombo.SelectedIndex = -1;
        _selection.SetSectionText("");
        _selection.SetSectionId(null);
        _selection.SetEvaluationId(null);
        EvaluationCombo.Items.Clear();
        TipoCombo.Items.Clear();
        UpdateRepoPreview();
        UpdateButtonStates();
    }

    private async void SectionCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing || SectionCombo.SelectedItem == null) return;
        var code = (string)SectionCombo.SelectedItem;
        _selection.SetSectionText(code);
        // Resolver la seccion por code DENTRO del curso seleccionado (no
        // globalmente) para no matchear otra seccion con el mismo code en
        // otro curso.
        var selectedCourseId = CursoCombo.SelectedItem is Course cc ? (long?)cc.Id : null;
        var row = _sections.FirstOrDefault(s => s.Code == code
            && (selectedCourseId == null || s.CourseId == selectedCourseId));
        row ??= _sections.FirstOrDefault(s => s.Code == code);
        _selection.SetSectionId(row?.Id);
        // Al cambiar de seccion se resetea la evaluacion (pertenece a otra seccion)
        _selection.SetEvaluationId(null);
        EvaluationCombo.Items.Clear();
        TipoCombo.Items.Clear();
        await LoadEvaluationsForSection(code, row?.Id);
        UpdateRepoPreview();
        UpdateButtonStates();
        _ = UpdateAssignmentsBanner();
    }

    private void EvaluationCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing || EvaluationCombo.SelectedItem == null) return;
        if (EvaluationCombo.SelectedItem is not Evaluation ev) return;
        // Id=0 => evaluacion sintetizada de fallback (Config.EvaluationTypes);
        // no hay id real que persistir.
        _selection.SetEvaluationId(ev.Id > 0 ? ev.Id : null);
        SyncTipoCombo(ev.Title);
        UpdatePdfButton();
    }

    // PDF de enunciado: visible solo si la evaluacion seleccionada tiene uno.
    private string? CurrentEvalPdfPath()
    {
        if (_selection.EvaluationId is not { } eid) return null;
        return _currentEvaluations.FirstOrDefault(e => e.Id == eid)?.ExamPdfPath;
    }

    private void UpdatePdfButton()
    {
        ViewPdfButton.Visibility = string.IsNullOrWhiteSpace(CurrentEvalPdfPath())
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ViewPdfButton_Click(object sender, RoutedEventArgs e)
    {
        var path = CurrentEvalPdfPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        ViewPdfButton.IsEnabled = false;
        var local = await ExamPdfService.DownloadAndOpenAsync(path);
        ViewPdfButton.IsEnabled = true;
        if (local == null)
            ShowToast("No se pudo abrir el enunciado. Reintenta.", ToastKind.Error);
    }

    /// <summary>
    /// Bloquea (deshabilita) curso/seccion/evaluacion cuando hay una tarea
    /// ACEPTADA y NO entregada, fijando la evaluacion aceptada. Asi el alumno no
    /// puede cambiar/borrar la seleccion mientras rinde (lo que cambiaba el
    /// evaluation_id del heartbeat y hacia parpadear el PC a offline). Se libera
    /// solo cuando entrega o el profe desactiva la evaluacion (la tarea deja de
    /// estar activa -> ya no hay aceptada-no-entregada).
    /// </summary>
    private void ApplyEvaluationLock(List<AssignmentStatus> statuses)
    {
        var locked = statuses.FirstOrDefault(s =>
            s.Accepted && !s.Submitted
            && s.Assignment.EvaluationId is { } id && id > 0);

        if (locked?.Assignment.EvaluationId is { } evalId)
        {
            if (_selection.EvaluationId != evalId)
                _selection.SetEvaluationId(evalId);

            for (int i = 0; i < EvaluationCombo.Items.Count; i++)
                if (EvaluationCombo.Items[i] is Evaluation ev && ev.Id == evalId
                    && EvaluationCombo.SelectedIndex != i)
                {
                    EvaluationCombo.SelectedIndex = i;
                    break;
                }

            CursoCombo.IsEnabled = false;
            SectionCombo.IsEnabled = false;
            EvaluationCombo.IsEnabled = false;
            ClearSelectionButton.IsEnabled = false;
            EvaluationCombo.ToolTip = "Evaluacion en curso: bloqueada hasta entregar o que el profesor la desactive.";
        }
        else
        {
            CursoCombo.IsEnabled = true;
            SectionCombo.IsEnabled = true;
            EvaluationCombo.IsEnabled = true;
            ClearSelectionButton.IsEnabled = true;
            EvaluationCombo.ToolTip = null;
        }
        UpdatePdfButton();
    }

    private async void AssignmentsLink_Click(object sender, RoutedEventArgs e) => await ShowAssignmentsDialog();

    private void SignupLink_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/signup");

    private async void LoginButton_Click(object sender, RoutedEventArgs e) => await DoLoginAsync();

    private async void LogoutButton_Click(object sender, RoutedEventArgs e) => await DoLogoutAsync();

    // Boton "Limpiar seleccion": resetea curso/seccion/evaluacion sin cerrar
    // sesion. Deshabilitado cuando hay una evaluacion bloqueada (aceptada-no-
    // entregada) para no romper el lock.
    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e) => ClearSelectors();

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.Toggle();
        UpdateThemeButton();
    }

    private void UpdateThemeButton()
        => ThemeButton.Content = ThemeService.IsDark ? "Tema claro" : "Tema oscuro";

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
        // _syncingTipo: SyncTipoCombo esta mutando TipoCombo para espejar la
        // evaluacion del sidebar; ignoramos ese rebote para no recalcular doble.
        if (_initializing || _syncingTipo) return;
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

    // Update MANUAL (no automatico, para no pegarle a la API de GitHub en cada
    // arranque). Chequea + descarga + reinicia si hay version nueva. Si no hay,
    // avisa y reactiva el boton.
    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        var original = UpdateButton.Content;
        UpdateButton.Content = "Buscando...";
        try
        {
            var willRestart = await UpdateService.CheckAndApplyAsync(msg => Log(msg), _gh.Token);
            if (!willRestart)
                ShowToast($"Ya estas en la ultima version (v{UpdateService.CurrentVersion()}).", ToastKind.Info);
            // Si willRestart == true, la app se reinicia sola; no reactivamos.
        }
        catch (Exception ex)
        {
            ShowToast($"No se pudo actualizar: {ex.Message}", ToastKind.Error);
        }
        finally
        {
            UpdateButton.Content = original;
            UpdateButton.IsEnabled = true;
        }
    }

    private void BuscarButton_Click(object sender, RoutedEventArgs e) => BuscarCarpeta();

    // Las acciones Crear/Clonar/Subir ahora se disparan desde el boton primario
    // contextual (PrimaryButton_Click -> _primaryAction). Ver UpdatePrimaryAction().

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
        var dlg = new LoginWindow(_gh, _selection) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            await UpdateSessionPanel();
            await EnrollIdentityAsync();
            Log("Sesion iniciada.");
        }
    }

    /// <summary>
    /// Intercambia el token de GitHub del alumno por el JWT de identidad
    /// verificada del backend (enroll-identity) y lo deja portado en el cliente
    /// Supabase. Best-effort: si no hay token o el enrolado falla, el cliente
    /// sigue usando el anon key crudo (no bloquea ni rompe el flujo). Se llama
    /// tras un login exitoso y al arrancar con sesion ya guardada.
    /// </summary>
    private async Task EnrollIdentityAsync()
    {
        try
        {
            var tok = _gh.Token;
            if (string.IsNullOrEmpty(tok)) return;
            await _sb.EnrollIdentityAsync(tok);
        }
        catch { }
    }

    private async Task DoLogoutAsync()
    {
        if (!_gh.IsAuthenticated) return;
        var r = MessageBox.Show("Cerrar sesion y borrar credenciales de este equipo?", "Cerrar sesion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        _gh.Logout();
        _sb.SetIdentityToken(null); // volver a anon: ya no hay identidad verificada
        ClearSelectors();
        Log("Sesion cerrada.");
        await UpdateSessionPanel();
    }

    /// <summary>
    /// Limpia los selectores (curso/seccion/evaluacion) y el estado persistido.
    /// Al cerrar sesion no debe quedar una seleccion vieja que el heartbeat siga
    /// reportando. Tambien reactiva los combos por si quedaron bloqueados por una
    /// evaluacion aceptada (ver ApplyEvaluationLock).
    /// </summary>
    private void ClearSelectors()
    {
        CursoCombo.IsEnabled = true;
        SectionCombo.IsEnabled = true;
        EvaluationCombo.IsEnabled = true;
        EvaluationCombo.ToolTip = null;

        CursoCombo.SelectedIndex = -1;
        SectionCombo.SelectedIndex = -1;
        EvaluationCombo.SelectedIndex = -1;
        EvaluationCombo.Items.Clear();
        TipoCombo.Items.Clear();

        _selection.SetSectionText("");
        _selection.SetSectionId(null);
        _selection.SetEvaluationId(null);

        // No dejar el enunciado descargado tras cerrar sesion / limpiar.
        ExamPdfService.DeleteAllDownloaded();
        UpdatePdfButton();
        UpdateRepoPreview();
        UpdateButtonStates();
    }

    // ===================== Modo UI =====================
    private void SetModoUi()
    {
        if (ModoExistente.IsChecked == true)
        {
            ReposCombo.IsEnabled = true; RefreshButton.IsEnabled = true;
            NombreBox.IsEnabled = false; TipoCombo.IsEnabled = false;
        }
        else
        {
            ReposCombo.IsEnabled = false; RefreshButton.IsEnabled = false;
            NombreBox.IsEnabled = true;
            // TipoCombo es read-only: espeja la evaluacion del sidebar.
            TipoCombo.IsEnabled = false;
        }
        UpdateRepoPreview();
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
        return RepoNameSanitizer.Sanitize($"{n}-{t}");
    }

    private void UpdateRepoPreview()
    {
        var repo = GetRepoName();
        if (repo != null) { RepoDestinoText.Text = repo; RepoDestinoText.Foreground = Brushes.Black; }
        else { RepoDestinoText.Text = ModoExistente.IsChecked == true ? "(selecciona un repositorio)" : "(rellenar nombre y tipo)"; RepoDestinoText.Foreground = Brushes.Gray; }
    }

    // Compat: el resto de la logica de negocio sigue llamando a este metodo.
    // Ahora solo recalcula la accion primaria contextual y el paso activo.
    private void UpdateButtonStates()
    {
        UpdatePrimaryAction();
        UpdateActiveStep();
    }

    /// <summary>
    /// Boton primario unico y contextual. Decide texto, color, handler y
    /// estado segun el estado actual. Reemplaza los antiguos botones
    /// "1. Crear" / "2. Subir". No cambia la logica de negocio: solo
    /// reconecta a los handlers existentes (CrearRepoAsync, SubirArchivosAsync).
    /// </summary>
    private void UpdatePrimaryAction()
    {
        var hasAuth = _gh.IsAuthenticated;
        var hasFolder = !string.IsNullOrEmpty(CarpetaBox.Text) && Directory.Exists(CarpetaBox.Text);
        var existente = ModoExistente.IsChecked == true;
        var hasRepoData = existente ? ReposCombo.SelectedItem != null
            : (!string.IsNullOrEmpty(NombreBox.Text.Trim()) && !string.IsNullOrEmpty((TipoCombo.SelectedItem as string ?? "").Trim()));

        // 1) Sin sesion.
        if (!hasAuth)
        {
            PrimaryButton.Content = "Inicia sesion primero";
            PrimaryButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            PrimaryButton.IsEnabled = false;
            _primaryAction = null;
            return;
        }

        // 3) Carpeta lista + repo (creado o clonado) -> Subir.
        if (hasFolder && hasRepoData)
        {
            PrimaryButton.Content = "Subir evaluacion";
            PrimaryButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Success;
            PrimaryButton.IsEnabled = true;
            _primaryAction = SubirArchivosAsync;
            return;
        }

        // 2) Datos completos -> Crear / Clonar.
        if (hasRepoData)
        {
            PrimaryButton.Content = existente ? "Clonar repositorio" : "Crear repositorio";
            PrimaryButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            PrimaryButton.IsEnabled = true;
            _primaryAction = CrearRepoAsync;
            return;
        }

        // Sesion iniciada pero faltan datos del repo.
        PrimaryButton.Content = existente ? "Selecciona un repositorio" : "Completa los datos";
        PrimaryButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        PrimaryButton.IsEnabled = false;
        _primaryAction = null;
    }

    /// <summary>
    /// Resalta el paso del sidebar que refleja el estado real del alumno:
    /// sin sesion = paso 1; con sesion sin repo+carpeta listos = paso 2;
    /// repo + carpeta listos = paso 3.
    /// </summary>
    private void UpdateActiveStep()
    {
        var hasAuth = _gh.IsAuthenticated;
        var hasFolder = !string.IsNullOrEmpty(CarpetaBox.Text) && Directory.Exists(CarpetaBox.Text);
        var existente = ModoExistente.IsChecked == true;
        var hasRepoData = existente ? ReposCombo.SelectedItem != null
            : (!string.IsNullOrEmpty(NombreBox.Text.Trim()) && !string.IsNullOrEmpty((TipoCombo.SelectedItem as string ?? "").Trim()));

        int active = !hasAuth ? 1 : (hasFolder && hasRepoData ? 3 : 2);

        SetStepActive(Step1Border, Step1Dot, Step1Num, Step1Title, active == 1, active > 1);
        SetStepActive(Step2Border, Step2Dot, Step2Num, Step2Title, active == 2, active > 2);
        SetStepActive(Step3Border, Step3Dot, Step3Num, Step3Title, active == 3, false);
    }

    private void SetStepActive(System.Windows.Controls.Border card, System.Windows.Controls.Border dot,
        System.Windows.Controls.TextBlock num, System.Windows.Controls.TextBlock title, bool isActive, bool isDone)
    {
        var primary = (Brush)FindResource("PrimaryBrush");
        var success = (Brush)FindResource("SuccessBrush");
        var surface2 = (Brush)FindResource("Surface2Brush");
        var border = (Brush)FindResource("BorderStrongBrush");
        var muted = (Brush)FindResource("MutedBrush");
        var text = (Brush)FindResource("TextBrush");

        // Numero original guardado en el Tag la primera vez.
        num.Tag ??= num.Text;
        var original = (string)num.Tag;

        if (isDone)
        {
            card.Background = Brushes.Transparent;
            dot.Background = success; dot.BorderBrush = success;
            num.Text = "✓"; num.Foreground = Brushes.White;
            title.Foreground = text;
        }
        else if (isActive)
        {
            card.Background = surface2;
            dot.Background = primary; dot.BorderBrush = primary;
            num.Text = original; num.Foreground = Brushes.White;
            title.Foreground = primary;
        }
        else
        {
            card.Background = Brushes.Transparent;
            dot.Background = surface2; dot.BorderBrush = border;
            num.Text = original; num.Foreground = muted;
            title.Foreground = muted;
        }
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _primaryAction;
        if (action == null) return;
        await action();
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

        // Chequear invitaciones pendientes SIEMPRE, sin importar cuantos repos
        // tenga el alumno: un alumno que ya posee >=1 repo igual puede tener
        // invitaciones nuevas sin aceptar. Antes esto vivia detras del gate
        // "if (count == 0)" y se perdia para alumnos que ya tenian repos.
        // null = no se pudo verificar (sentinel de error), distinto de lista
        // vacia = no hay invitaciones.
        var invites = await _gh.GetPendingInvitationsAsync();
        if (invites == null)
        {
            Log("No se pudo verificar invitaciones (error de red/API). Reintenta.");
        }
        else if (invites.Count > 0)
        {
            Log($"Tienes {invites.Count} invitacion(es) pendiente(s).");
            if (await AcceptInvitationsAsync(invites)) { await Task.Delay(2000); await LoadUserReposAsync(); return; }
        }

        if (count == 0)
        {
            var asg = await GetSectionAssignmentsAsync();
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

        // Tareas activas de la seccion para mapear invitacion -> assignment por
        // prefijo de slug y registrar la aceptacion en BD.
        var asg = await GetSectionAssignmentsAsync();
        var me = _user?.Login;
        var evalOrg = CurrentEvaluationOrg();

        int ok = 0;
        var urls = new List<string>();
        foreach (var inv in invites)
        {
            if (!await _gh.AcceptInvitationAsync(inv.Id)) continue;
            ok++;
            var repoFullName = inv.Repository?.FullName;
            if (inv.Repository != null) urls.Add($"https://github.com/{repoFullName}");

            // record_acceptance SINCRONO antes de cualquier recompute del banner:
            // cierra el transitorio "aceptada en GitHub pero sin reconciliar en
            // BD". Al await aqui, el recompute posterior (LoadUserReposAsync /
            // UpdateAssignmentsBanner) ya ve la aceptacion registrada. Reusa el
            // MISMO matcher LONGEST-PREFIX-WINS que el banner para no divergir y
            // evitar que un slug corto registre la aceptacion contra la tarea
            // equivocada.
            if (!string.IsNullOrEmpty(me))
            {
                var repoName = inv.Repository?.Name ?? "";
                var match = ClassroomRepoMatcher.PickByLongestPrefix(
                    asg, repoName, inv.Inviter?.Login, evalOrg,
                    a => a.Title, a => a.Org);
                if (match != null)
                {
                    var repoUrl = repoFullName != null
                        ? $"https://github.com/{repoFullName}"
                        : $"https://github.com/{me}/{repoName}";
                    await _sb.RecordAcceptanceAsync(me, match.Id, match.Title,
                        _selection.SectionText, repoName, repoUrl, _selection.EvaluationId);
                }
            }
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
        await _sb.ReportStudentActivityAsync("create_repo", _user!.Login, _user.Email, Environment.MachineName, _selection.SectionText, repo, url, _selection.SectionId);
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
                await ShowLocalTrapLockAsync(repo, clean.FilesCount, clean.FilesNames);
                UpdateButtonStates();
                return;
            }
            Log("OK Repo limpio.");
        }

        CarpetaBox.Text = target;
        OpenPythonIdle(target);
        await _sb.ReportStudentActivityAsync("clone", _user!.Login, _user.Email, Environment.MachineName, _selection.SectionText, repo, $"https://github.com/{owner}/{name}", _selection.SectionId);
        await RecordAcceptanceIfClassroomRepoAsync(name, $"https://github.com/{owner}/{name}");
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
        await _sb.ReportStudentActivityAsync("upload", _user!.Login, _user.Email, Environment.MachineName, _selection.SectionText, repo, res.Url, _selection.SectionId);
        // Captura el enlace como ENTREGA formal en el panel (el alumno no tiene
        // que apretar "Entregar repo" aparte). Best-effort, no bloquea.
        await RecordSubmissionIfClassroomRepoAsync(name, res.Url!);

        // Termino la evaluacion: borrar el enunciado descargado (no debe quedar
        // registro local que se pueda divulgar).
        ExamPdfService.DeleteAllDownloaded();

        // tipo ahora es el titulo de la evaluacion (BD) o el tipo legacy
        // (Config.EvaluationTypes en fallback). Ya no mapeamos via switch: el
        // titulo es la etiqueta real que el alumno ve en el AVA.
        var tipoLabel = !string.IsNullOrEmpty(tipo) ? tipo : "la evaluacion correspondiente";
        MessageBox.Show($"Entrega subida correctamente.\n\nURL (copiada al portapapeles):\n{res.Url}\n\nProximo paso:\n1. Abre el AVA\n2. Ve a {tipoLabel}\n3. Pega el enlace (Ctrl+V)\n4. Envia", "Listo - Entrega en el AVA", MessageBoxButton.OK, MessageBoxImage.Information);

        var del = MessageBox.Show(
            "Ya terminaste la evaluacion?\n\nSi presionas SI:\n" +
            $"  - Se elimina la carpeta local ({folder}); el repo en GitHub se mantiene.\n" +
            "  - Se cierra tu sesion de GitHub.\n" +
            "  - Se libera el internet del equipo.\n\n" +
            "El PC queda listo para el siguiente alumno.",
            "Finalizar evaluacion?", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (del == MessageBoxResult.Yes)
        {
            try { Directory.Delete(folder, true); CarpetaBox.Text = ""; Log("Carpeta local eliminada."); UpdateButtonStates(); } catch { }
            await FinishExamCleanupAsync();
        }
    }

    /// <summary>
    /// Cierre de evaluacion tras entregar: libera internet/Copilot de ESTE PC y
    /// cierra la sesion de GitHub para que el equipo no quede "con internet
    /// tomado" ni logueado para el siguiente alumno (PC de laboratorio
    /// compartido). Al limpiar la seleccion, el evaluation_id queda en null y el
    /// AdminTick cae al control global -> no se vuelve a bloquear.
    /// </summary>
    private async Task FinishExamCleanupAsync()
    {
        try { InternetBlockService.Unblock(); _internetBlocked = false; } catch { }
        try
        {
            if (_copilotBlocked)
            {
                CopilotBlockService.OnCheatDetected -= OnCopilotCheatDetected;
                CopilotBlockService.Unblock();
                _copilotBlocked = false;
            }
        }
        catch { }
        try { _gh.Logout(); } catch { }
        try { _sb.SetIdentityToken(null); } catch { } // volver a anon para el siguiente alumno
        ClearSelectors();
        Log("Evaluacion finalizada: internet liberado y sesion de GitHub cerrada.");
        try { await UpdateSessionPanel(); } catch { }
    }

    // ===================== Classroom assignments =====================

    /// <summary>
    /// Confirma la matricula del alumno contra el roster (RPC get_my_enrollment,
    /// no-PII) y cachea el resultado en _enrollment. Es ADITIVO: el alumno sigue
    /// eligiendo su seccion como hasta ahora; esto SOLO agrega una confirmacion
    /// (y un endurecimiento opcional de EXPECTED cuando hay match).
    ///
    /// NUNCA bloquea: si no hay sesion o seccion, o si la RPC no responde
    /// (Confirmed=false), se cae al comportamiento por defecto sin endurecer.
    /// </summary>
    private async Task RefreshEnrollmentAsync()
    {
        var me = _user?.Login;
        var sectionId = _selection.SectionId;
        if (string.IsNullOrEmpty(me) || sectionId is not { } sid)
        {
            // Sin datos para consultar: no hay confirmacion. No es "no
            // matriculado"; simplemente no se intento. Comportamiento por
            // defecto (sin endurecer EXPECTED).
            _enrollment = null;
            return;
        }

        _enrollment = await _sb.GetMyEnrollmentAsync(me, sid);
    }

    /// <summary>
    /// true solo cuando hay una matricula CONFIRMADA con match en la seccion
    /// actual. Unica condicion bajo la que se endurece EXPECTED. En no-match o
    /// no-confirmado (red caida) devuelve false => comportamiento por defecto.
    /// </summary>
    private bool RosterMatchConfirmed()
    {
        var sectionId = _selection.SectionId;
        return _enrollment is { Confirmed: true, Found: true }
            && sectionId is { } sid
            && _enrollment.SectionId == sid;
    }

    private List<Assignment> FilterBySection(List<Assignment> all)
    {
        var sec = _selection.SectionText.Trim().ToUpperInvariant();
        return all.Where(a =>
        {
            var s = (a.Section ?? "").Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(s) || (!string.IsNullOrEmpty(sec) && s == sec);
        }).ToList();
    }

    /// <summary>
    /// Tareas activas que el alumno DEBE ver. ROBUSTO al evaluation_id: trae
    /// todas las activas, filtra por SECCION, y si la evaluacion seleccionada
    /// tiene tareas las prioriza; si NO (link huerfano porque se recreo la
    /// evaluacion), cae a las de la seccion. Antes el filtro exigia
    /// evaluation_id exacto en el servidor: al recrear una evaluacion, la tarea
    /// quedaba huerfana y "desaparecia" (o quedaba pegada la vieja). Ahora la
    /// evaluacion es una preferencia, no un gate.
    /// </summary>
    private async Task<List<Assignment>> GetSectionAssignmentsAsync()
    {
        var all = FilterBySection(await _sb.GetActiveAssignmentsAsync(null));
        var evalId = _selection.EvaluationId;
        if (evalId is { } id)
        {
            var matched = all.Where(a => a.EvaluationId == id).ToList();
            if (matched.Count > 0) return matched;
        }
        return all;
    }

    /// <summary>
    /// Org efectiva de la evaluacion activa (para el desempate de invitaciones
    /// por inviter-org). Resuelve la Evaluation cargada que matchea el
    /// evaluation_id activo; null si no hay evaluacion resuelta.
    /// </summary>
    private string? CurrentEvaluationOrg()
    {
        var evalId = _selection.EvaluationId;
        if (evalId is not { } id) return null;
        return _currentEvaluations.FirstOrDefault(e => e.Id == id)?.Org;
    }

    /// <summary>
    /// Determina, para cada assignment de la seccion, su estado segun las 5
    /// senales: OWNED (repo esperado existe), ACCEPTED_DB (assignment_acceptances),
    /// SUBMITTED (assignment_submissions), INVITED (repository_invitations) y
    /// EXPECTED (la propia lista asg). Las invitaciones se asocian por PREFIJO
    /// de slug ({slug}-) con desempate por inviter-org vs Evaluation.Org/Assignment.Org.
    ///
    /// El parametro invitations puede ser null (no se pudo consultar la API de
    /// invitaciones): en ese caso InvitationPending queda en false y el caller
    /// debe distinguir "desconocido" de "0 invitaciones".
    ///
    /// unassociatedInvitations recibe las invitaciones vivas que NO matchean
    /// ninguna tarea esperada, para que el banner las muestre como
    /// "invitaciones sin asociar" en vez de descartarlas.
    /// </summary>
    private async Task<List<AssignmentStatus>> ComputeAssignmentStatusesAsync(
        List<Assignment> asg,
        List<RepoInvitation>? invitations,
        List<RepoInvitation> unassociatedInvitations)
    {
        var result = new List<AssignmentStatus>();
        if (asg.Count == 0)
        {
            // Sin tareas esperadas, toda invitacion viva queda sin asociar.
            if (invitations != null) unassociatedInvitations.AddRange(invitations);
            return result;
        }

        // Sin sesion no podemos cruzar contra repos; usamos solo acceptances
        // si hubiera username, pero sin user todo queda pendiente.
        var me = _user?.Login;

        // Repos del alumno (para detectar el repo esperado de cada tarea).
        var repoNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reposByName = new Dictionary<string, GitHubRepo>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(me) && _gh.IsAuthenticated)
        {
            foreach (var r in await _gh.ListReposAsync())
            {
                repoNames.Add(r.Name);
                reposByName[r.Name] = r;
            }
        }

        // Aceptaciones registradas en BD.
        var acceptedIds = new HashSet<long>();
        if (!string.IsNullOrEmpty(me))
            foreach (var a in await _sb.GetAcceptancesAsync(me))
                acceptedIds.Add(a.AssignmentId);

        // Entregas formales registradas en BD.
        var submittedIds = new HashSet<long>();
        var submissionsByAssignment = new Dictionary<long, Submission>();
        if (!string.IsNullOrEmpty(me))
            foreach (var s in await _sb.GetSubmissionsAsync(me))
            {
                submittedIds.Add(s.AssignmentId);
                submissionsByAssignment[s.AssignmentId] = s;
            }

        // Invitaciones vivas pendientes de consumir. Se van quitando de este set
        // a medida que se asocian a una tarea, para luego reportar las sobrantes
        // como "sin asociar". null = no se pudo consultar (no es lista vacia).
        var remainingInvites = invitations != null
            ? new List<RepoInvitation>(invitations)
            : new List<RepoInvitation>();

        foreach (var a in asg)
        {
            string? repoName = null;
            string? repoUrl = null;
            bool hasRepo = false;

            if (!string.IsNullOrEmpty(me))
            {
                var expected = ClassroomRepoNaming.ExpectedClassroomRepo(a.Title, me);
                if (repoNames.Contains(expected))
                {
                    hasRepo = true;
                    repoName = expected;
                    var owner = reposByName.TryGetValue(expected, out var r) && r.Owner != null
                        ? r.Owner.Login : me;
                    repoUrl = $"https://github.com/{owner}/{expected}";
                }
            }

            var accepted = hasRepo || acceptedIds.Contains(a.Id);
            var submitted = submittedIds.Contains(a.Id);
            submissionsByAssignment.TryGetValue(a.Id, out var sub);

            // Endurecimiento de EXPECTED por roster (solo con match confirmado):
            // una tarea EXPECTED-only (no la posee, no la acepto, no la entrego)
            // de seccion GLOBAL/vacia se omite, porque con matricula confirmada
            // conocemos la seccion exacta del alumno y no necesitamos la pista
            // global. CRITICO: las tareas que el alumno posee/acepto/entrego
            // (hasRepo || accepted || submitted) SIEMPRE pasan, sin importar su
            // seccion ni el roster -> una entrega pendiente real (pendienteEntregar)
            // NUNCA se suprime. Sin match confirmado, nada se omite (default).
            // Esto solo filtra que filas EXPECTED entran a result ANTES del
            // bucketing de 5 senales: no toca filas OWNED/ACCEPTED/SUBMITTED ni
            // INVITED, asi que el algebra de 3 buckets disjuntos se preserva.
            if (RosterMatchConfirmed()
                && !hasRepo && !accepted && !submitted
                && string.IsNullOrEmpty(a.Section))
            {
                continue;
            }

            // INVITED: la asociacion invitacion<->tarea se resuelve mas abajo en
            // un paso aparte (longest-prefix-wins), porque procesar las tareas en
            // el orden de asg permitiria que un slug corto ("tarea-") robe la
            // invitacion de uno mas especifico ("tarea-extra-"). Aqui solo se
            // arma el AssignmentStatus; InvitationId/InvitationPending se rellenan
            // luego con el match determinista.
            result.Add(new AssignmentStatus
            {
                Assignment = a,
                Accepted = accepted,
                RepoName = repoName,
                RepoUrl = repoUrl,
                Submitted = submitted,
                SubmittedRepoUrl = sub?.RepoUrl,
                SubmittedAt = sub?.SubmittedAt
            });
        }

        // Asociacion determinista invitacion -> tarea con LONGEST-PREFIX-WINS:
        // se procesan las invitaciones contra las tareas ordenadas por prefijo
        // descendente (mas especifico primero), de modo que "tarea-extra-"
        // reclame "tarea-extra-login" antes de que "tarea-" lo capture. Cada
        // invitacion se asigna a lo sumo a una tarea; el orden de salida (result)
        // se mantiene en el orden original de asg.
        foreach (var inv in remainingInvites.ToList())
        {
            var match = MatchAssignmentForRepo(result, inv);
            if (match == null) continue;
            match.InvitationId = inv.Id;
            match.InvitationPending = true;
            remainingInvites.Remove(inv);
        }

        // Invitaciones vivas que no matchearon ninguna tarea esperada.
        unassociatedInvitations.AddRange(remainingInvites);
        return result;
    }

    /// <summary>
    /// Resuelve, para una invitacion de repo, la tarea (AssignmentStatus) a la
    /// que pertenece usando LONGEST-PREFIX-WINS. Solo considera tareas aun sin
    /// invitacion asociada (InvitationPending=false) y delega el algoritmo de
    /// matching al core compartido ClassroomRepoMatcher.PickByLongestPrefix para
    /// que el banner y AcceptInvitationsAsync usen EXACTAMENTE la misma logica.
    /// </summary>
    private AssignmentStatus? MatchAssignmentForRepo(
        List<AssignmentStatus> statuses, RepoInvitation inv)
    {
        var repoName = inv.Repository?.Name ?? "";
        if (repoName.Length == 0) return null;

        // Solo tareas que aun no tienen invitacion: asi cada invitacion se
        // asigna a lo sumo a una tarea (matching bipartito).
        var unclaimed = statuses.Where(s => !s.InvitationPending).ToList();
        var match = ClassroomRepoMatcher.PickByLongestPrefix(
            unclaimed.Select(s => s.Assignment),
            repoName, inv.Inviter?.Login, CurrentEvaluationOrg(),
            a => a.Title, a => a.Org);
        if (match == null) return null;
        return unclaimed.FirstOrDefault(s => ReferenceEquals(s.Assignment, match));
    }

    private async Task UpdateAssignmentsBanner()
    {
        // Confirmar matricula contra el roster ANTES de calcular (el resultado
        // endurece EXPECTED solo con match; ComputeAssignmentStatusesAsync lo
        // consulta via RosterMatchConfirmed). Es aditivo y nunca bloquea.
        await RefreshEnrollmentAsync();

        var asg = await GetSectionAssignmentsAsync();

        // Las invitaciones son verdad VIVA y se consultan SIEMPRE (no detras del
        // gate de "0 repos"): un alumno que ya posee >=1 repo igual puede tener
        // invitaciones nuevas sin aceptar. null = no se pudo verificar (sentinel
        // de error), distinto de lista vacia = no hay invitaciones.
        var invitations = await _gh.GetPendingInvitationsAsync();
        var unassociated = new List<RepoInvitation>();
        var statuses = await ComputeAssignmentStatusesAsync(asg, invitations, unassociated);

        // Si hay una tarea ACEPTADA y NO entregada, bloquear la seleccion de
        // curso/seccion/evaluacion en esa evaluacion. Evita que el alumno (o un
        // repoblado del combo) borre/cambie el evaluation_id durante la
        // rendicion -> el heartbeat se mantiene estable y el PC no parpadea a
        // offline. Se libera al entregar o cuando el profe desactiva la eval.
        ApplyEvaluationLock(statuses);

        // Sentinel de error: si la API de invitaciones fallo, NO afirmar "0
        // pendientes". El alumno debe saber que el dato no se pudo verificar.
        if (invitations == null)
        {
            AssignmentsBannerText.Text =
                "No se pudo verificar invitaciones. Reintenta o avisa al profesor.";
            AssignmentsBanner.Visibility = Visibility.Visible;
            // No podemos afirmar que haya o no pendientes: dejar el link visible
            // para que el alumno pueda abrir el dialogo y reintentar.
            AssignmentsLink.Visibility = Visibility.Visible;
            return;
        }

        // 5-senales -> 3 buckets DISJUNTOS (nunca sumar conjuntos solapados):
        //   pendienteAceptar  = EXPECTED ∩ INVITED − OWNED − ACCEPTED_DB
        //   esperandoInvite   = EXPECTED − INVITED − OWNED − ACCEPTED_DB − SUBMITTED
        //   pendienteEntregar = (OWNED ∨ ACCEPTED_DB) ∧ ¬SUBMITTED
        var pendienteAceptar = statuses.Count(s =>
            s.InvitationPending && !s.Accepted);
        var esperandoInvite = statuses.Count(s =>
            !s.InvitationPending && !s.Accepted && !s.Submitted);
        var pendienteEntregar = statuses.Count(s =>
            s.Accepted && !s.Submitted);

        // Pendientes accionables: equivalente exacto, en el algebra de 5 senales,
        // del antiguo `pending = !Accepted && !Submitted` de roster-client. Manda
        // la visibilidad del link "Aceptar tareas" (que abre el dialogo). Las
        // invitaciones sin asociar son SOLO informativas y no cuentan aqui.
        var pendingActionable = pendienteAceptar + esperandoInvite + pendienteEntregar;

        var partes = new List<string>();
        // Conteo primario del banner: pendienteAceptar.
        if (pendienteAceptar > 0)
            partes.Add($"Tienes {pendienteAceptar} tarea(s) pendientes de aceptar");
        if (esperandoInvite > 0)
            partes.Add($"{esperandoInvite} esperando invitacion del profesor");
        if (pendienteEntregar > 0)
            partes.Add($"{pendienteEntregar} pendientes de entregar");
        if (unassociated.Count > 0)
            partes.Add($"{unassociated.Count} invitacion(es) sin asociar");

        // Nota suave de matricula (roster-client R3): cuando NO hay match
        // confirmado (no matriculado o no se pudo confirmar) se avisa, pero NUNCA
        // se bloquea ni se ponen los contadores en cero. Las entregas pendientes
        // (por github_username) se siguen mostrando igual. Compone con el banner
        // de 5 senales: la nota es una linea adicional, no reemplaza los buckets.
        var note = EnrollmentSoftNote();

        if (partes.Count > 0 || note != null)
        {
            // Buckets en una linea (separados por ' · '); la nota suave, si la
            // hay, va en una linea aparte debajo.
            var lineas = new List<string>();
            if (partes.Count > 0) lineas.Add(string.Join(" · ", partes));
            if (note != null) lineas.Add(note);
            AssignmentsBannerText.Text = string.Join("\n", lineas);
            AssignmentsBanner.Visibility = Visibility.Visible;
            // El link "Aceptar tareas" solo tiene sentido si hay pendientes
            // accionables (no por una nota de matricula sola ni por invitaciones
            // sin asociar, que son informativas).
            AssignmentsLink.Visibility = pendingActionable > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            AssignmentsBanner.Visibility = Visibility.Collapsed;
            AssignmentsLink.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Texto suave de confirmacion de matricula (o null si no corresponde).
    /// SOLO informativo: nunca bloquea ni altera contadores. Se muestra cuando
    /// hay sesion + seccion elegidas pero el roster no confirma el match:
    ///   - Confirmed=false  => "No pudimos confirmar tu matricula" (red caida).
    ///   - Confirmed + !Found => "No encontramos tu matricula..." (no en roster).
    /// Con match confirmado (o sin datos para consultar) no se muestra nota.
    /// </summary>
    private string? EnrollmentSoftNote()
    {
        // Sin sesion o seccion no intentamos confirmar: sin nota.
        if (string.IsNullOrEmpty(_user?.Login) || _selection.SectionId is null)
            return null;

        if (_enrollment is null)
            return null;

        if (RosterMatchConfirmed())
            return null;

        if (!_enrollment.Confirmed)
            return "No pudimos confirmar tu matricula (sin conexion). Igual puedes trabajar normalmente.";

        // Confirmado pero sin match en el roster de esta seccion.
        return "No encontramos tu matricula en esta seccion. Revisa la seccion elegida o avisa al profesor.";
    }

    private async Task ShowAssignmentsDialog()
    {
        var asg = await GetSectionAssignmentsAsync();
        if (asg.Count == 0) { MessageBox.Show("No hay tareas activas.", "Sin tareas", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        // Consultar invitaciones para mostrar el estado "Invitacion pendiente"
        // en el dialogo (null = no se pudo verificar; el dialogo igual abre).
        var invitations = await _gh.GetPendingInvitationsAsync();
        var unassociated = new List<RepoInvitation>();
        var statuses = await ComputeAssignmentStatusesAsync(asg, invitations, unassociated);
        var dlg = new AssignmentsWindow(statuses, OpenAcceptUrl, (u) => OpenUrl(u), SubmitRepo) { Owner = this };
        dlg.ShowDialog();
        // Al cerrar, refrescar el banner por si el alumno acepto o entrego algo.
        await UpdateAssignmentsBanner();
    }

    /// <summary>
    /// Si el repo clonado corresponde a una tarea activa de Classroom de la
    /// seccion del alumno ({slug}-{username}), registra la aceptacion en BD.
    /// Asi queda registro aunque el alumno clone directo sin pasar por el banner.
    /// </summary>
    private async Task RecordAcceptanceIfClassroomRepoAsync(string repoName, string repoUrl)
    {
        var me = _user?.Login;
        if (string.IsNullOrEmpty(me)) return;
        var asg = await GetSectionAssignmentsAsync();
        foreach (var a in asg)
        {
            if (string.Equals(ClassroomRepoNaming.ExpectedClassroomRepo(a.Title, me), repoName, StringComparison.OrdinalIgnoreCase))
            {
                await _sb.RecordAcceptanceAsync(me, a.Id, a.Title, _selection.SectionText, repoName, repoUrl, _selection.EvaluationId);
                break;
            }
        }
    }

    /// <summary>
    /// Tras subir al repo, registra la ENTREGA (assignment_submissions) capturando
    /// el enlace, para que el profe la vea en el panel ("entrego" + URL) sin que el
    /// alumno tenga que apretar "Entregar repo" aparte. Mapea el repo a la tarea por
    /// nombre esperado; si no hay match exacto pero hay UNA sola tarea activa para la
    /// evaluacion, usa esa (los slugs de Classroom no siempre coinciden con
    /// Sanitize(titulo)). No bloquea la subida: cualquier fallo se ignora.
    /// </summary>
    private async Task RecordSubmissionIfClassroomRepoAsync(string repoName, string repoUrl)
    {
        var me = _user?.Login;
        if (string.IsNullOrEmpty(me)) return;
        try
        {
            var asg = await GetSectionAssignmentsAsync();
            if (asg.Count == 0) return;
            foreach (var a in asg)
            {
                if (string.Equals(ClassroomRepoNaming.ExpectedClassroomRepo(a.Title, me), repoName, StringComparison.OrdinalIgnoreCase))
                {
                    await _sb.RecordSubmissionAsync(a.Id, me, repoUrl);
                    return;
                }
            }
            // Fallback: una sola tarea activa => atribuir la entrega a esa.
            if (asg.Count == 1)
                await _sb.RecordSubmissionAsync(asg[0].Id, me, repoUrl);
        }
        catch { }
    }

    /// <summary>
    /// Abre la URL de aceptacion de una tarea en el navegador embebido y
    /// registra la aceptacion en BD para que el profesor la vea. La aceptacion
    /// se registra SINCRONAMENTE (await) antes de abrir el navegador, de modo
    /// que el recompute del banner al cerrar el dialogo ya vea la fila en BD.
    /// Es async void porque es un callback de AssignmentsWindow (Action), pero
    /// el await ocurre mientras el dialogo sigue abierto.
    /// </summary>
    private async void OpenAcceptUrl(AssignmentStatus status)
    {
        var a = status.Assignment;
        var me = _user?.Login;
        if (!string.IsNullOrEmpty(me))
        {
            var repoName = status.RepoName ?? ClassroomRepoNaming.ExpectedClassroomRepo(a.Title, me);
            var repoUrl = status.RepoUrl ?? $"https://github.com/{me}/{repoName}";
            await _sb.RecordAcceptanceAsync(me, a.Id, a.Title, _selection.SectionText, repoName, repoUrl, _selection.EvaluationId);
        }
        if (!string.IsNullOrEmpty(a.ClassroomUrl))
            OpenUrl(a.ClassroomUrl, onClosed: () => _ = RefreshReposAfterAcceptAsync());
    }

    /// <summary>
    /// Pide la URL del repo y registra la entrega formal en BD.
    /// Pre-fill: si ya tiene repo detectado (Classroom), usa esa URL.
    /// </summary>
    private async void SubmitRepo(AssignmentStatus status)
    {
        var me = _user?.Login;
        if (string.IsNullOrEmpty(me))
        {
            MessageBox.Show("Debes iniciar sesion con GitHub para entregar.", "Sin sesion", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Pre-fill con repoUrl si existe (aceptado via Classroom).
        var prefill = !string.IsNullOrEmpty(status.RepoUrl)
            ? status.RepoUrl
            : !string.IsNullOrEmpty(status.SubmittedRepoUrl)
                ? status.SubmittedRepoUrl
                : "";

        var input = SimpleInputDialog("URL del repositorio a entregar:", "Entregar repositorio", prefill);
        if (string.IsNullOrWhiteSpace(input)) return;

        await _sb.RecordSubmissionAsync(status.Assignment.Id, me, input.Trim());
        Log($"Entrega registrada: {status.Title} -> {input.Trim()}");
        MessageBox.Show("Entrega registrada correctamente.", "Entregado", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Dialogo simple de input de texto (WPF no trae uno nativo).</summary>
    private string? SimpleInputDialog(string prompt, string title, string prefill = "")
    {
        var dlg = new Window
        {
            Title = title,
            Width = 460,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush")
        };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        var label = new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Style = (Style)FindResource("LabelText"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        var input = new System.Windows.Controls.TextBox
        {
            Text = prefill,
            FontSize = 14,
            Padding = new Thickness(8, 6, 8, 6)
        };
        var btnRow = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var okBtn = new Wpf.Ui.Controls.Button
        {
            Content = "Entregar",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Success,
            Padding = new Thickness(20, 6, 20, 6)
        };
        var cancelBtn = new Wpf.Ui.Controls.Button
        {
            Content = "Cancelar",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            Padding = new Thickness(20, 6, 20, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        panel.Children.Add(label);
        panel.Children.Add(input);
        panel.Children.Add(btnRow);
        dlg.Content = panel;

        string? result = null;
        okBtn.Click += (_, _) => { result = input.Text; dlg.Close(); };
        cancelBtn.Click += (_, _) => dlg.Close();
        input.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { result = input.Text; dlg.Close(); } };

        dlg.ShowDialog();
        return result;
    }

    // ===================== Admin polling =====================
    private async Task AdminTickAsync()
    {
        // Refresca el JWT de identidad si esta por expirar (no-op si no hay
        // identidad o todavia no vence). Antes del resto del poll para que las
        // llamadas de este tick ya viajen con un token vigente.
        await _sb.EnsureIdentityFreshAsync();
        await CheckAdminConfigAsync();
        await RefreshBlocklistAsync();
        await UpdateAssignmentsBanner();
        await SendHeartbeatAsync();
        await CheckTargetedLockdownAsync();
        await CheckUpdateRequestAsync();
        await CheckNetworkProbeAsync();
    }

    // Sonda de red (deteccion de contacto a Copilot). Throttle + dedup para no
    // spamear ni pesar: corre cada 30s y reporta cada (host+source) a lo sumo
    // una vez cada 5 min. Es EVIDENCIA para revision, no veredicto.
    private DateTime _lastNetProbeUtc = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _reportedAiHits = new();

    private async Task CheckNetworkProbeAsync()
    {
        if (_user == null) return;
        if ((DateTime.UtcNow - _lastNetProbeUtc).TotalSeconds < 30) return;
        _lastNetProbeUtc = DateTime.UtcNow;

        List<NetworkProbeService.Finding> findings;
        try { findings = await Task.Run(() => NetworkProbeService.Probe()); }
        catch (Exception ex) { Log($"[NetProbe] fallo: {ex.Message}"); return; }

        foreach (var f in findings)
        {
            var key = $"{f.Host}|{f.Source}";
            if (_reportedAiHits.TryGetValue(key, out var last)
                && (DateTime.UtcNow - last).TotalMinutes < 5) continue;
            _reportedAiHits[key] = DateTime.UtcNow;
            Log($"[NetProbe] contacto Copilot: {f.Host} ({f.Detail})");
            try
            {
                await _sb.ReportStudentActivityAsync(
                    "ai_endpoint_contacted", _user.Login, _user.Email, Environment.MachineName,
                    _selection.SectionText, f.Host, f.Detail, _selection.SectionId);
            }
            catch (Exception ex) { Log($"[NetProbe] reporte fallo: {ex.Message}"); }
        }
    }

    // Arranque del cliente (UTC) y ultimo update_requested_at ya procesado.
    private readonly DateTime _processStartUtc = DateTime.UtcNow;
    private string? _lastUpdateRequestProcessed;

    /// <summary>
    /// Update DISPARADO POR EL PROFE desde el panel (NO automatico). El profe
    /// setea control.update_requested_at = NOW(); el cliente actualiza UNA vez
    /// si ese timestamp es POSTERIOR a su arranque. Asi no le pega a la API de
    /// GitHub en cada tick (solo cuando el profe lo pide) ni relanza el update
    /// en cada arranque por un request viejo. La lectura de control ya se hace
    /// cada tick (Supabase, barato); GitHub solo se toca al disparar.
    /// </summary>
    private async Task CheckUpdateRequestAsync()
    {
        var ctl = await _sb.GetControlAsync();
        var raw = ctl?.UpdateRequestedAt;
        if (string.IsNullOrEmpty(raw)) return;
        if (raw == _lastUpdateRequestProcessed) return; // ya procesado
        if (!DateTimeOffset.TryParse(raw, out var reqDto)) return;
        if (reqDto.UtcDateTime <= _processStartUtc)
        {
            // Request anterior a este arranque: marcar como visto, no actualizar.
            _lastUpdateRequestProcessed = raw;
            return;
        }
        _lastUpdateRequestProcessed = raw;
        Log("[update] el profesor pidio actualizar. Buscando version nueva...");
        await UpdateService.CheckAndApplyAsync(msg => Log(msg), _gh.Token); // reinicia si hay update
    }

    /// <summary>
    /// Refresca el blocklist efectivo de la seccion del alumno. Si el fetch
    /// falla, GetBlocklistAsync devuelve null y dejamos _blocklist en null
    /// (=> IsSuspicious cae a Config.SuspiciousProcesses). Como AdminTickAsync
    /// corre en el arranque (antes del primer SendHeartbeatAsync), la primera
    /// deteccion ya usa la lista fetcheada si la red responde.
    /// </summary>
    private async Task RefreshBlocklistAsync()
    {
        // section_id (multi-evaluacion) es preferido; cae a section TEXT si es
        // null (forward-compat con clientes viejos).
        _blocklist = await _sb.GetBlocklistAsync(_selection.SectionText, _selection.SectionId);
    }

    // Override de pantalla por PC, en sincrono (para los checkStillLocked de las
    // CheatWindow). true => el profe desbloqueo la pantalla de ESTE PC -> liberar.
    private bool ScreenUnblockedSync()
        => Task.Run(() => _sb.IsPcScreenUnblockedAsync(Environment.MachineName).GetAwaiter().GetResult())
            .GetAwaiter().GetResult();

    // Predicados de "sigue bloqueada la pantalla" para los checkStillLocked de las
    // CheatWindow. Fail-safe: solo libera si el profe desbloqueo ESTE PC por
    // nombre (ScreenUnblockedSync) Y el backend ya no reporta el lock. Antes
    // estaban como 3 lambdas casi identicas inline; consolidadas aca.

    // Lockdown REMOTO (force-only).
    private bool StillLockedByForce()
        => !ScreenUnblockedSync()
            && Task.Run(() => _sb.IsForceLockdownAsync(_selection.EvaluationId)).GetAwaiter().GetResult();

    // Lockdown DIRIGIDO (pc+usuario) o force. pc varia: Environment.MachineName
    // en el dirigido remoto, el pc real en la trampa local.
    private bool StillLockedByTargetOrForce(string pc, string me)
        => !ScreenUnblockedSync()
            && Task.Run(() =>
                _sb.IsTargetedLockedAsync(pc, me).GetAwaiter().GetResult()
                || _sb.IsForceLockdownAsync(_selection.EvaluationId).GetAwaiter().GetResult()).GetAwaiter().GetResult();

    private async Task CheckAdminConfigAsync()
    {
        // Control EFECTIVO de la evaluacion actual: override por evaluacion ??
        // global id=1 (no el global pelado). Si no se puede resolver (red/null)
        // degradamos CERRADO: no tocamos el estado actual (return) en vez de
        // soltar un bloqueo activo. La APERTURA (aca) y la LIBERACION
        // (IsForceLockdownAsync en checkStillLocked) leen EXACTAMENTE la misma
        // resolucion y comparten el cache _lastKnownLock: esta llamada de
        // apertura ya SIEMBRA el cache (lo hace GetEffectiveControlAsync), asi el
        // primer poll de liberacion -aunque su fetch falle- retiene el lock
        // recien aplicado en vez de soltarlo.
        var cfg = await _sb.GetEffectiveControlAsync(_selection.EvaluationId);
        if (cfg == null) return;

        // Override por PC (desbloqueo por nombre de equipo, sin depender del
        // usuario). unblock_internet anula el bloqueo de internet/Copilot de ESTE
        // PC; unblock_screen libera la pantalla roja. Fail-safe: si el fetch falla
        // ovr es null -> no se libera nada.
        var ovr = await _sb.GetPcOverrideAsync(Environment.MachineName);
        bool effInternet = cfg.InternetBlock && !(ovr?.UnblockInternet ?? false);
        bool screenUnblocked = ovr?.UnblockScreen ?? false;

        if (effInternet && !_internetBlocked) { Log("[ADMIN] Bloqueo de internet activado."); InternetBlockService.Block(); _internetBlocked = true; }
        else if (!effInternet && _internetBlocked) { Log("[ADMIN] Bloqueo de internet desactivado."); InternetBlockService.Unblock(); _internetBlocked = false; }

        // Copilot: amarrado al mismo toggle que internet. Sabotea el settings.json
        // de VS Code y monta un watcher que detecta si el alumno reactiva Copilot.
        if (effInternet && !_copilotBlocked)
        {
            CopilotBlockService.OnCheatDetected += OnCopilotCheatDetected;
            CopilotBlockService.Block();
            _copilotBlocked = true;
            Log("[ADMIN] Bloqueo de Copilot activado.");
        }
        else if (!effInternet && _copilotBlocked)
        {
            CopilotBlockService.OnCheatDetected -= OnCopilotCheatDetected;
            CopilotBlockService.Unblock();
            _copilotBlocked = false;
            Log("[ADMIN] Bloqueo de Copilot desactivado.");
        }

        // La pantalla roja remota SOLO debe saltar en modo evaluacion: el control
        // global force_lockdown no debe bloquear a un alumno que no esta rindiendo.
        // Sin evaluation_id activo (no acepto/selecciono evaluacion) NO se bloquea.
        bool inExam = _selection.EvaluationId is { } examEvalId && examEvalId > 0;
        if (cfg.ForceLockdown && inExam && !screenUnblocked && !_remoteLockdownActive)
        {
            _remoteLockdownActive = true;
            Log("[ADMIN] Lockdown remoto activado.");
            // Heartbeat inmediato (fire-and-forget) para que el panel vea a ESTE PC
            // como bloqueado al instante; el AdminTick queda detenido tras ShowDialog.
            _ = SendHeartbeatAsync();
            var alert = new CheatWindow("(remoto)", 0, new[] { "Lockdown remoto del profesor" }, remoteSource: true,
                checkStillLocked: StillLockedByForce,
                onHeartbeat: () => _ = SendHeartbeatAsync());
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

    /// <summary>
    /// Handler que se dispara cuando el FileSystemWatcher de CopilotBlockService
    /// detecta que el alumno edito el settings.json para reactivar Copilot.
    /// Reporta el cheat al panel (visible para el profesor) y aplica lockdown
    /// inmediato en la maquina del alumno. Se invoca desde un thread del watcher;
    /// Log/Status/ShowToast ya dispatchean al UI thread internamente.
    /// </summary>
    private async void OnCopilotCheatDetected()
    {
        Log("[CHEAT] Intento de reactivacion de Copilot detectado.");

        // Reportar al panel via el mismo canal de alertas de procesos sospechosos.
        try
        {
            var user = _user?.Login ?? "(unknown)";
            await _sb.ReportProcessAlertAsync(
                user,
                Environment.MachineName,
                _selection.SectionText,
                "copilot-reactivation",
                "Intento de reactivar Copilot editando settings.json");
        }
        catch (Exception ex) { Log($"[CHEAT] Reporte de Copilot fallo: {ex.Message}"); }

        // Lockdown inmediato en la maquina del alumno (marker + auto-start + TaskMgr off).
        try
        {
            LockdownService.Trigger("(copilot)", 0, new[] { "Reactivacion de Copilot en settings.json" });
            Log("[CHEAT] Lockdown aplicado por reactivacion de Copilot.");
        }
        catch (Exception ex) { Log($"[CHEAT] Lockdown por Copilot fallo: {ex.Message}"); }

        // Avisar al alumno
        ShowToast("Se detecto intento de reactivar Copilot. Prueba bloqueada.", ToastKind.Error);
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
            _ = SendHeartbeatAsync();
            var alert = new CheatWindow("(dirigido)", 0, new[] { reason }, remoteSource: true,
                checkStillLocked: () => StillLockedByTargetOrForce(Environment.MachineName, me),
                onHeartbeat: () => _ = SendHeartbeatAsync());
            alert.ShowDialog();
            _targetedLockdownActive = false;
        }
    }

    /// <summary>
    /// Pantalla roja por TRAMPA LOCAL (repo sucio, navegacion prohibida), ahora
    /// VISIBLE en el panel y LIBERABLE remoto. Reporta el auto-lock a
    /// targeted_lockdowns (via RPC) para que el profe lo vea y lo desbloquee; si
    /// el reporte se confirma, la pantalla re-chequea cada 10s y se libera cuando
    /// el profe apaga el lock (que ademas limpia el marker persistente). Si el
    /// reporte FALLA (offline), cae a password-only: fail-safe, nunca auto-libera.
    /// Reusa _targetedLockdownActive => visible como "active" en el heartbeat y
    /// evita que CheckTargetedLockdownAsync abra una segunda pantalla.
    /// </summary>
    private async Task ShowLocalTrapLockAsync(string reasonOrRepo, int filesCount, string[] filesNames)
    {
        if (_targetedLockdownActive) return; // ya hay pantalla roja activa
        _targetedLockdownActive = true;

        LockdownService.Trigger(reasonOrRepo, filesCount, filesNames);

        var me = _user?.Login ?? "";
        var pc = Environment.MachineName;
        try
        {
            await _sb.ReportCheatEventAsync(
                me.Length > 0 ? me : "(sin sesion)", pc, reasonOrRepo, filesCount, filesNames);
        }
        catch { }

        bool reported = await _sb.ReportSelfLockAsync(
            pc, me, _selection.SectionText, filesNames.FirstOrDefault() ?? reasonOrRepo);

        _ = SendHeartbeatAsync();
        CheatWindow alert = reported
            ? new CheatWindow(reasonOrRepo, filesCount, filesNames, remoteSource: true,
                checkStillLocked: () => StillLockedByTargetOrForce(pc, me),
                onHeartbeat: () => _ = SendHeartbeatAsync())
            : new CheatWindow(reasonOrRepo, filesCount, filesNames);
        alert.Owner = this;
        alert.ShowDialog();

        _targetedLockdownActive = false;
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
            if (!_lastProcSet.Contains(key) && ProcessMonitor.IsSuspicious(p.Name, _blocklist))
                await _sb.ReportProcessAlertAsync(_user.Login, Environment.MachineName, _selection.SectionText, p.Name, p.Title);
        }
        _lastProcSet.Clear();
        foreach (var k in current) _lastProcSet.Add(k);

        var internetState = InternetBlockService.IsBlocked() ? "blocked" : "free";
        var lockdownState = (_remoteLockdownActive || _targetedLockdownActive) ? "active" : "none";
        // Atribuye la presencia a la evaluacion actual del alumno. El ON CONFLICT
        // de online_clients NO cambia en este slice (sigue pc_name+github_username);
        // el aislamiento de re-rendiciones por evaluacion es PR5.
        await _sb.SendHeartbeatAsync(Environment.MachineName, _user.Login, _user.Email,
            _selection.SectionText, procs, internetState, lockdownState,
            _selection.EvaluationId, UpdateService.CurrentVersion());
    }

    // ===================== Helpers =====================
    public void Log(string msg)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Log(msg)); return; }

        // Historial completo en memoria (visible en "Ver detalles").
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        _logBuffer.AppendLine(line);
        _logWindow?.AppendLine(line);

        // Ultimo mensaje en la barra de estado.
        StatusText.Text = msg;

        // Toast para eventos clave (login, repo creado/clonado, subida, errores).
        var kind = LogClassifier.Classify(msg);
        if (kind != null) ShowToast(msg, kind.Value);
    }

    public void Status(string msg)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Status(msg)); return; }
        StatusText.Text = msg;
    }

    // ===================== Toast =====================
    public void ShowToast(string msg, ToastKind kind)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ShowToast(msg, kind)); return; }

        var accent = kind switch
        {
            ToastKind.Success => (Brush)FindResource("SuccessBrush"),
            ToastKind.Error => (Brush)FindResource("DangerBrush"),
            _ => (Brush)FindResource("InfoBrush"),
        };
        ToastAccent.Background = accent;
        ToastBorder.BorderBrush = accent;
        ToastText.Text = msg;

        ToastBorder.Visibility = Visibility.Visible;

        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        ToastBorder.BeginAnimation(OpacityProperty, fadeIn);

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer!.Stop();
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (_, _) => ToastBorder.Visibility = Visibility.Collapsed;
            ToastBorder.BeginAnimation(OpacityProperty, fadeOut);
        };
        _toastTimer.Start();
    }

    private void LogDetailsLink_Click(object sender, RoutedEventArgs e)
    {
        if (_logWindow == null)
        {
            _logWindow = new LogDetailWindow { Owner = this };
            _logWindow.Closed += (_, _) => _logWindow = null;
            _logWindow.SetText(_logBuffer.ToString());
            _logWindow.Show();
        }
        else
        {
            _logWindow.Activate();
        }
    }

    // Abre la URL en el navegador embebido (WebView2) endurecido. NO hay
    // fallback al navegador externo: si WebView2 falla, la propia ventana avisa
    // y se cierra. El navegador filtra por whitelist y, ante un dominio
    // prohibido, llama a OnForbiddenNavigation para disparar la trampa.
    private void OpenUrl(string url, Action? onClosed = null)
    {
        var ctx = new BrowseContext
        {
            GithubUsername = _user?.Login ?? "",
            PcName = Environment.MachineName,
            Section = _selection.SectionText
        };
        var win = new WebBrowserWindow(url, "Navegador", ctx, OnForbiddenNavigation) { Owner = this };
        if (onClosed != null) win.Closed += (_, _) => onClosed();
        win.Show();
    }

    /// <summary>
    /// Tras aceptar una tarea en Classroom, GitHub tarda unos segundos en crear
    /// el repo (en la org, con el alumno como colaborador). Antes el alumno tenia
    /// que apretar "Refrescar" a mano y, si lo hacia muy rapido, el repo todavia
    /// no existia => "no aparece el repo". Aca poll-eamos hasta ~15s tras cerrar
    /// el navegador de aceptacion y refrescamos la lista cuando aparece.
    /// </summary>
    private async Task RefreshReposAfterAcceptAsync()
    {
        int before;
        try { before = (await _gh.ListReposAsync()).Count; }
        catch { before = -1; }

        for (int i = 0; i < 6; i++)
        {
            await Task.Delay(2500);
            int now;
            try { now = (await _gh.ListReposAsync()).Count; }
            catch { continue; }
            if (now != before) break; // aparecio el repo recien creado
        }

        await LoadUserReposAsync();
        await UpdateAssignmentsBanner();
    }

    /// <summary>
    /// Trampa por navegacion fuera de la whitelist. Mismo flujo que cuando se
    /// detecta un repo sucio: persiste el lockdown, lo reporta a BD y muestra la
    /// pantalla roja (CheatWindow). Se ejecuta en el contexto del MainWindow,
    /// que tiene acceso a _sb, _user y la seccion.
    /// </summary>
    private async void OnForbiddenNavigation(string host)
    {
        var reason = $"Navegacion prohibida: {host}";
        Log($"TRAMPA: {reason}");

        await ShowLocalTrapLockAsync(reason, 0, new[] { reason });
        UpdateButtonStates();
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

// LogDetailWindow se movio a su propio archivo (Windows/LogDetailWindow.cs)
// como primer sub-paso del desacople de MainWindow (ENT-6).
