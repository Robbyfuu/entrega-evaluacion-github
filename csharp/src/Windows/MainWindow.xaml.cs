using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
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
public partial class MainWindow : Window, ILogSink, IUserNotifier, IRedScreenHost
{
    private readonly IGitHubService _gh;
    private readonly ISupabaseClient _sb;

    // Seleccion del alumno (seccion + evaluacion) detras de ISelectionStore, en
    // reemplazo de la clase estatica global StudentSection. Se inyecta por el
    // constructor desde el composition root (App.StartShell), igual que _gh/_sb
    // (ENT-6 step 5).
    private readonly ISelectionStore _selection;

    // Colaboradores extraidos del polling admin (ENT-7 extractions #2 y #3).
    // Dependen solo de _sb (ya inyectado) y, los que loguean, de este MainWindow
    // como ILogSink; por eso se CONSTRUYEN en el constructor en vez de inyectarse
    // desde el composition root. Cada uno es duenio del estado que antes vivia
    // aca: HeartbeatReporter el prior-set de procesos (_lastProcSet);
    // NetworkProbeReporter el throttle/dedup de la sonda (_lastNetProbeUtc/
    // _reportedAiHits); RemoteUpdateWatcher el arranque y el one-shot del update
    // (_processStartUtc/_lastUpdateRequestProcessed).
    private readonly HeartbeatReporter _heartbeat;
    private readonly BlocklistRefresher _blocklistRefresher;
    private readonly NetworkProbeReporter _networkProbe;
    private readonly RemoteUpdateWatcher _remoteUpdate;

    // Servicio del countdown anti-tamper (ENT-31 slice 3). Mantiene el ancla
    // server-authoritative (hora del servidor + fin del examen) y tickea con un
    // Stopwatch MONOTONICO, nunca con el reloj de pared. Se re-ancla en el tick
    // admin (SyncExamTimeAsync) cuando hay evaluacion en curso; la slice 4 leera
    // ExamTimer.Remaining para pintar el widget. No depende de _sb.
    private readonly ExamTimerService _examTimer;

    // Guard del cierre de la ventana (ENT-8 slice O): bloquea el cierre durante
    // la evaluacion y solo lo permite con la clave del profesor. Es duenio del
    // estado que antes vivia aca (_allowExit + _lastCloseReport). Se construye en
    // el constructor porque depende de _sb/_selection (ya inyectados), de este
    // MainWindow como IUserNotifier y de callbacks que cierran sobre estado de la
    // vista (UpdateService.IsApplying, el modal de clave, el cierre autorizado y
    // la identidad _user).
    private readonly ExitGuard _exitGuard;

    // Coordinador de la ruta de ENTREGA (ENT-8 slice B): saca de este code-behind
    // la orquestacion del push (mensaje de commit + GitService via factory +
    // Task.Run) y el registro best-effort de entregas/aceptaciones contra las
    // tareas de Classroom. Se construye en el constructor porque depende de _gh/
    // _sb/_selection (ya inyectados) y de este MainWindow como ILogSink/
    // IUserNotifier; la UI (validacion XAML, MessageBox, portapapeles, prompts)
    // se queda aca. Es duenio de GetSectionAssignmentsAsync/FilterBySection (antes
    // aqui), que solo leen _selection + _sb.
    private readonly SubmissionCoordinator _submission;

    // Coordinador del LOCKDOWN / pantalla roja (ENT-8 slice K): saca de este
    // code-behind la orquestacion del bloqueo de internet/Copilot, la pantalla
    // roja (remota, dirigida y trampa local), la suscripcion del watcher de
    // Copilot y los 4 flags de lockdown (ahora son SUYOS, no de MainWindow). Se
    // construye en el constructor porque depende de _sb/_selection (ya inyectados),
    // de este MainWindow como ILogSink/IUserNotifier/IRedScreenHost, del heartbeat
    // (SendHeartbeatAsync) y de la identidad (_user). El dedup + MessageBox del
    // mensaje del profesor (concern aparte) se queda aca.
    private readonly LockdownCoordinator _lockdown;

    // Colaborador de I/O del PDF de enunciado (ENT-7 extraction #4). No tiene
    // dependencias (envuelve llamadas estaticas a ExamPdfService), asi que se
    // inicializa inline en vez de construirse en el constructor como los de arriba.
    private readonly PdfViewer _pdfViewer = new();

    // Estado
    private GitHubUser? _user;
    // Dedup del mensaje del profesor (concern SEPARADO del lockdown). Los 4 flags
    // de lockdown se movieron a LockdownCoordinator (ENT-8 slice K); este dedup se
    // queda porque la lectura del mensaje + MessageBox sigue en la vista.
    private string _lastAdminMessage = "";

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

    // Widget flotante del countdown (ENT-31 slice 4). Instancia unica perezosa: se
    // crea la primera vez que aplica mostrarlo (minimizado + en evaluacion + sin
    // lockdown) y se reutiliza con Show/Hide. NO se le setea Owner (un owned window
    // se auto-oculta al minimizar el owner, justo cuando lo necesitamos visible),
    // por eso hay que cerrarlo explicitamente al cerrar la app.
    private WidgetWindow? _widget;

    // Icono de bandeja (NotifyIcon). La app arranca y vive OCULTA en la bandeja;
    // este icono es el unico punto de re-entrada visible (doble clic / "Abrir") y
    // la salida ("Salir", que SIEMPRE enruta por el ExitGuard, nunca un Shutdown
    // directo). Vive lo que vive el proceso y se libera en Closed (cierre real).
    private TaskbarIcon? _trayIcon;

    public MainWindow(IGitHubService gh, ISupabaseClient sb, ISelectionStore selection)
    {
        // Las dependencias se asignan ANTES de InitializeComponent y de cualquier
        // otro codigo del cuerpo del constructor que pudiera usarlas: en C# los
        // inicializadores de campo ya corrieron, asi que estos campos solo quedan
        // definidos una vez que el composition root los entrega aqui.
        _gh = gh;
        _sb = sb;
        _selection = selection;

        // Colaboradores del polling admin: _sb ya esta asignado y este MainWindow
        // ya es un ILogSink valido, asi que se pueden construir aqui (no tocan UI).
        // RemoteUpdateWatcher captura aqui el arranque (UTC) y lee _gh.Token de
        // forma diferida (en el instante del disparo), igual que el original.
        _heartbeat = new HeartbeatReporter(_sb, this);
        _blocklistRefresher = new BlocklistRefresher(_sb);
        _networkProbe = new NetworkProbeReporter(_sb, this);
        _remoteUpdate = new RemoteUpdateWatcher(_sb, this, () => _gh.Token);

        // Timer del examen: sin dependencias (el ancla se siembra en el tick admin
        // via SyncExamTimeAsync). Se construye aca junto al resto de colaboradores.
        _examTimer = new ExamTimerService();

        // Guard del cierre: la decision pura vive en ExitDecision; el throttle del
        // reporte reusa ProbeThrottle. Los callbacks reproducen EXACTO los efectos
        // que antes vivian inline en OnClosing: isUpdating => UpdateService.IsApplying;
        // passwordPrompt => modal de clave del profesor (Owner=this); onAuthorizedExit
        // => Unregister + DeleteAllDownloaded + Shutdown (cada uno best-effort);
        // currentUser => la identidad del alumno (_user), leida de forma diferida.
        _exitGuard = new ExitGuard(
            _sb, _selection, this,
            () => UpdateService.IsApplying,
            () => new PasswordPromptWindow { Owner = this }.ShowDialog() == true,
            () =>
            {
                try { DaemonService.Unregister(); } catch { }
                try { ExamPdfService.DeleteAllDownloaded(); } catch { }
                Application.Current.Shutdown();
            },
            () => _user);

        // Coordinador de entrega: _gh/_sb/_selection ya asignados y este MainWindow
        // ya es un ILogSink/IUserNotifier valido. La factory construye el GitService
        // con (token, nombre, email) igual que el codigo inline previo; el push lee
        // el token vigente de _gh dentro de PushAsync.
        _submission = new SubmissionCoordinator(
            _gh, _sb, _selection, this, this,
            (token, name, email) => new GitService(token, name, email));

        // Coordinador de lockdown: _sb/_selection ya asignados y este MainWindow ya
        // es un ILogSink/IUserNotifier/IRedScreenHost valido. El heartbeat se pasa
        // como delegado (SendHeartbeatAsync, que ahora lee _lockdown.IsLockdownActive)
        // y la identidad se lee de forma diferida (() => _user). El host de la
        // pantalla roja es este MainWindow (construye el CheatWindow en el UI thread).
        _lockdown = new LockdownCoordinator(
            _sb, _selection, this, this,
            SendHeartbeatAsync, this, () => _user);

        InitializeComponent();

        // Icono de bandeja: la app vive oculta y se restaura/sale desde aqui.
        BuildTrayIcon();

        // Los combos se poblan asincronicamente en InitAsync (fetch BD + fallback
        // a Config.cs), igual que _blocklist. No poblamos aca para evitar datos
        // legacy antes de saber si la BD responde.
        Loaded += async (_, _) => await InitAsync();

        // ENT-31 slice 4: el widget del countdown depende del estado de la ventana.
        // StateChanged re-evalua su visibilidad (mostrar solo al minimizar, en
        // evaluacion y sin lockdown). Closed (cierre real, con ShutdownMode =
        // OnExplicitShutdown) cierra el widget (sin Owner no se cierra solo) y
        // libera el icono de bandeja.
        StateChanged += (_, _) => UpdateWidgetVisibility();
        Closed += (_, _) =>
        {
            _widget?.Close();
            _widget = null;
            _trayIcon?.Dispose();
            _trayIcon = null;
        };
    }

    /// <summary>
    /// Arma el icono de la bandeja del sistema (Hardcodet TaskbarIcon, puro WPF).
    /// La app corre OCULTA: este icono es el punto de re-entrada. Doble clic y
    /// "Abrir" restauran via <see cref="RestoreFromTray"/>; "Salir" enruta SIEMPRE
    /// por el ExitGuard (clave del profesor durante la evaluacion), NUNCA un
    /// Shutdown directo. El icono se resuelve por pack URI; si falla, el icono
    /// queda sin imagen pero el menu sigue operativo (best-effort).
    /// </summary>
    private void BuildTrayIcon()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "Abrir" };
        openItem.Click += (_, _) => RestoreFromTray();

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Salir" };
        exitItem.Click += (_, _) => RequestExit();

        menu.Items.Add(openItem);
        menu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Entrega de Evaluacion a GitHub",
            ContextMenu = menu,
            Visibility = Visibility.Visible,
        };

        try
        {
            _trayIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Resources/app.ico"));
        }
        catch { }

        _trayIcon.TrayMouseDoubleClick += (_, _) => RestoreFromTray();
    }

    /// <summary>
    /// Restaura la ventana principal desde la bandeja (o desde minimizada) y la
    /// trae al frente. Secuencia canonica usada por TODOS los puntos de
    /// restauracion (doble clic e item "Abrir" de la bandeja, callback del widget
    /// ENT-31 y la activacion cross-proceso del segundo lanzamiento).
    /// </summary>
    public void RestoreFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>
    /// Salida solicitada desde la bandeja ("Salir"). Enruta SIEMPRE por el
    /// ExitGuard: durante la evaluacion exige la clave del profesor; nunca cierra
    /// con un Shutdown directo. Primero restaura la ventana para que el prompt de
    /// clave (Owner=this) tenga un duenio visible y llegue al frente. Si no se
    /// autoriza, no pasa nada (la app sigue viva en la bandeja).
    /// </summary>
    public void RequestExit()
    {
        RestoreFromTray();
        _exitGuard.HandleClosing(alreadyCancelled: false);
    }

    /// <summary>
    /// ENT-31 slice 4: muestra el widget flotante del countdown SOLO cuando la
    /// ventana esta MINIMIZADA, hay una evaluacion en curso (EvaluationId &gt; 0) y
    /// NO hay lockdown activo; en cualquier otro caso lo oculta. La pantalla roja
    /// SIEMPRE gana: este gate nunca muestra el widget mientras IsLockdownActive, y
    /// <see cref="IRedScreenHost.ShowBlocking"/> ademas lo oculta explicitamente
    /// antes del modal y re-evalua al volver. Solo lee estado; no toca el lockdown.
    /// </summary>
    private void UpdateWidgetVisibility()
    {
        bool shouldShow =
            WindowState == WindowState.Minimized &&
            !_lockdown.IsLockdownActive &&
            _selection.EvaluationId > 0;

        if (!shouldShow)
        {
            _widget?.Hide();
            return;
        }

        // Creacion perezosa: el restante sale SOLO de ExamTimerService (slice 3);
        // el callback de restaurar es lo unico con que el widget toca esta ventana.
        _widget ??= new WidgetWindow(
            () => _examTimer.Remaining,
            RestoreFromTray);
        _widget.PositionTopRight();
        _widget.Show();
    }

    // El programa NO se cierra durante la evaluacion: el alumno no puede salir
    // del control (y el daemon lo relanzaria igual). Solo se cierra con la clave
    // del profesor (ruta autorizada del ExitGuard). Fuera de evaluacion la X
    // OCULTA a la bandeja en vez de cerrar (el monitoreo sigue vivo: ShutdownMode
    // = OnExplicitShutdown). "Durante la evaluacion" usa la MISMA condicion que el
    // resto de la vista: hay una evaluacion en curso (_selection.EvaluationId > 0).
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        // Durante la evaluacion: el cierre lo gobierna ENTERO el ExitGuard (gate,
        // toast "no puedes cerrar", reporte del intento con throttle y escape por
        // clave del profesor). Sin cambios respecto del comportamiento original.
        if (_selection.EvaluationId > 0)
        {
            if (_exitGuard.HandleClosing(e.Cancel)) e.Cancel = true;
            return;
        }

        // Fuera de evaluacion: un cierre REAL ya autorizado (bandeja "Salir" ->
        // ExitGuard -> Shutdown, o un update aplicandose) debe pasar. Cualquier
        // otro cierre (la X) se cancela y la ventana se oculta en la bandeja; la
        // app sigue monitoreando en segundo plano. La unica salida real sigue
        // siendo la ruta autorizada del ExitGuard.
        if (_exitGuard.ShouldAllowClose(e.Cancel)) return;
        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
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
        // Decision pura (SectionId-first, Code-fallback) en el core; la vista solo
        // consume la fila resuelta para seleccionar curso/seccion abajo.
        var savedRow = SelectionCascadeResolver.ResolveSavedSection(
            savedCode, savedSectionId, _sections, s => s.Id, s => s.Code);

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
        // El core decide los codigos (filtra por curso, o cae a Config.Sections
        // cuando no hay secciones fetcheadas); la vista solo los agrega al combo.
        var codes = SelectionCascadeResolver.ResolveSectionCodes(
            courseId, _sections, Config.Sections, s => s.CourseId, s => s.Code);
        foreach (var code in codes) SectionCombo.Items.Add(code);
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

        // El core decide QUE mostrar (fetcheadas tal cual; sintesis Id=0 SOLO con
        // sectionId==null; vacio cuando la BD viva no trae ninguna). La vista
        // construye los items de fallback (asi Core no referencia Evaluation) y los
        // agrega. _currentEvaluations queda con lo FETCHEADO, no con lo sintetizado.
        var toShow = SelectionCascadeResolver.ResolveEvaluationsToShow(
            sectionId, _currentEvaluations, Config.EvaluationTypes,
            t => new Evaluation { Id = 0, Title = t, Active = true });
        foreach (var ev in toShow) EvaluationCombo.Items.Add(ev);
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
        var ok = await _pdfViewer.TryOpenAsync(path);
        ViewPdfButton.IsEnabled = true;
        if (!ok)
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
        // El core resuelve CUAL evaluacion bloquear (Accepted && !Submitted &&
        // EvaluationId>0); la vista aplica el lock (SelectedIndex con el guard
        // != i, deshabilitar combos, tooltip) o lo libera.
        var lockedEvalId = SelectionCascadeResolver.ResolveLockedEvaluationId(
            statuses, s => s.Accepted, s => s.Submitted, s => s.Assignment.EvaluationId);

        if (lockedEvalId is { } evalId)
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
    /// Lee el estado real de la UI (sesion, carpeta, modo, datos de repo) y delega
    /// la decision al core <see cref="PrimaryActionResolver"/>. Unica fuente de
    /// lectura del estado para el boton primario y el paso activo, asi ambos no
    /// pueden divergir. Sin logica de negocio aqui: solo proyecta los controles a
    /// los 4 booleans puros que el resolver consume.
    /// </summary>
    private PrimaryActionResolution ResolvePrimaryAction()
    {
        var hasAuth = _gh.IsAuthenticated;
        var hasFolder = !string.IsNullOrEmpty(CarpetaBox.Text) && Directory.Exists(CarpetaBox.Text);
        var existente = ModoExistente.IsChecked == true;
        var hasRepoData = existente ? ReposCombo.SelectedItem != null
            : (!string.IsNullOrEmpty(NombreBox.Text.Trim()) && !string.IsNullOrEmpty((TipoCombo.SelectedItem as string ?? "").Trim()));

        return PrimaryActionResolver.Resolve(hasAuth, hasFolder, existente, hasRepoData);
    }

    /// <summary>
    /// Boton primario unico y contextual. Resuelve el estado en el core y aplica el
    /// <see cref="PrimaryActionKind"/> a la UI/handler. No cambia la logica de
    /// negocio: solo reconecta a los handlers existentes (CrearRepoAsync,
    /// SubirArchivosAsync).
    /// </summary>
    private void UpdatePrimaryAction() => ApplyPrimaryAction(ResolvePrimaryAction().Kind);

    /// <summary>
    /// Traduce el <see cref="PrimaryActionKind"/> resuelto en el core al texto,
    /// apariencia, estado habilitado y handler del boton primario. Todo el wiring de
    /// UI/handler vive aqui (la vista), no en el core.
    /// </summary>
    private void ApplyPrimaryAction(PrimaryActionKind kind)
    {
        switch (kind)
        {
            // 1) Sin sesion.
            case PrimaryActionKind.LoginRequired:
                PrimaryButton.Content = "Inicia sesion primero";
                PrimaryButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
                PrimaryButton.IsEnabled = false;
                _primaryAction = null;
                break;

            // 3) Carpeta lista + repo (creado o clonado) -> Subir.
            case PrimaryActionKind.Submit:
                PrimaryButton.Content = "Subir evaluacion";
                PrimaryButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Success;
                PrimaryButton.IsEnabled = true;
                _primaryAction = SubirArchivosAsync;
                break;

            // 2) Datos completos, modo nuevo -> Crear.
            case PrimaryActionKind.CreateRepo:
                PrimaryButton.Content = "Crear repositorio";
                PrimaryButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                PrimaryButton.IsEnabled = true;
                _primaryAction = CrearRepoAsync;
                break;

            // 2) Datos completos, modo existente -> Clonar (mismo handler que Crear).
            case PrimaryActionKind.CloneRepo:
                PrimaryButton.Content = "Clonar repositorio";
                PrimaryButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                PrimaryButton.IsEnabled = true;
                _primaryAction = CrearRepoAsync;
                break;

            // Sesion iniciada pero falta elegir repositorio (modo existente).
            case PrimaryActionKind.SelectRepo:
                PrimaryButton.Content = "Selecciona un repositorio";
                PrimaryButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
                PrimaryButton.IsEnabled = false;
                _primaryAction = null;
                break;

            // Sesion iniciada pero faltan nombre/tipo (modo nuevo).
            case PrimaryActionKind.CompleteData:
                PrimaryButton.Content = "Completa los datos";
                PrimaryButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
                PrimaryButton.IsEnabled = false;
                _primaryAction = null;
                break;
        }
    }

    /// <summary>
    /// Resalta el paso del sidebar que refleja el estado real del alumno:
    /// sin sesion = paso 1; con sesion sin repo+carpeta listos = paso 2;
    /// repo + carpeta listos = paso 3. El paso lo decide el core; aqui solo se
    /// aplica al UI.
    /// </summary>
    private void UpdateActiveStep()
    {
        var active = ResolvePrimaryAction().ActiveStep;

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
            var asg = await _submission.GetSectionAssignmentsAsync();
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
        var asg = await _submission.GetSectionAssignmentsAsync();
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
                await _lockdown.ShowLocalTrapAsync(repo, clean.FilesCount, clean.FilesNames);
                UpdateButtonStates();
                return;
            }
            Log("OK Repo limpio.");
        }

        CarpetaBox.Text = target;
        OpenPythonIdle(target);
        await _sb.ReportStudentActivityAsync("clone", _user!.Login, _user.Email, Environment.MachineName, _selection.SectionText, repo, $"https://github.com/{owner}/{name}", _selection.SectionId);
        await _submission.RecordAcceptanceIfClassroomRepoAsync(_user!.Login, name, $"https://github.com/{owner}/{name}");
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

        // Push en hilo de fondo (Task.Run dentro del coordinador). El armado del
        // mensaje de commit, la construccion del GitService y el diagnostico
        // (Status/Log) viven ahora en PushAsync; aca solo se decide segun el
        // resultado. El resultado + URL llegan ANTES de cualquier limpieza.
        var res = await _submission.PushAsync(folder, owner, name, repo, nombre, _user!.Email ?? "", tipo);
        if (!res.Ok) return;

        try { Clipboard.SetText(res.RepoUrl!); } catch { }
        await _sb.ReportStudentActivityAsync("upload", _user!.Login, _user.Email, Environment.MachineName, _selection.SectionText, repo, res.RepoUrl, _selection.SectionId);
        // Captura el enlace como ENTREGA formal en el panel (el alumno no tiene
        // que apretar "Entregar repo" aparte). Best-effort, no bloquea.
        await _submission.RecordSubmissionIfClassroomRepoAsync(_user!.Login, name, res.RepoUrl!);

        // Termino la evaluacion: borrar el enunciado descargado (no debe quedar
        // registro local que se pueda divulgar).
        ExamPdfService.DeleteAllDownloaded();

        // tipo ahora es el titulo de la evaluacion (BD) o el tipo legacy
        // (Config.EvaluationTypes en fallback). Ya no mapeamos via switch: el
        // titulo es la etiqueta real que el alumno ve en el AVA.
        var tipoLabel = !string.IsNullOrEmpty(tipo) ? tipo : "la evaluacion correspondiente";
        MessageBox.Show($"Entrega subida correctamente.\n\nURL (copiada al portapapeles):\n{res.RepoUrl}\n\nProximo paso:\n1. Abre el AVA\n2. Ve a {tipoLabel}\n3. Pega el enlace (Ctrl+V)\n4. Envia", "Listo - Entrega en el AVA", MessageBoxButton.OK, MessageBoxImage.Information);

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
        // Slice internet/Copilot del cierre delegado al coordinador (duenio de los
        // flags y del watcher de Copilot). El resto (logout/identidad/selectores/
        // panel) se queda aca.
        _lockdown.ReleaseForExamEnd();
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
    /// Orquesta el estado de cada tarea de la seccion: hace los fetches de I/O
    /// (repos, aceptaciones, entregas) y delega el algebra PURA de las 5 senales
    /// (OWNED / ACCEPTED_DB / SUBMITTED / INVITED / EXPECTED) + la asociacion de
    /// invitaciones (longest-prefix) a EntregaEvaluacion.Core
    /// AssignmentStatusCalculator. Los resultados puros se mapean de vuelta al
    /// view-model WPF AssignmentStatus para el banner y el dialogo.
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
        // Sin tareas esperadas, no hace falta NINGUN fetch (repos/aceptaciones/
        // entregas): toda invitacion viva queda sin asociar. Restaura el
        // short-circuit previo a ENT-7 para no pegarle a GitHub/Supabase en una
        // seccion sin assignments (paridad exacta de I/O).
        if (asg.Count == 0)
        {
            if (invitations != null) unassociatedInvitations.AddRange(invitations);
            return new List<AssignmentStatus>();
        }

        // Sin sesion no podemos cruzar contra repos; usamos solo acceptances
        // si hubiera username, pero sin user todo queda pendiente.
        var me = _user?.Login;

        // ===== I/O: insumos del calculo (lo unico que NO es puro) =====

        // Repos del alumno (para detectar el repo esperado de cada tarea).
        var repos = new List<RepoInput>();
        if (!string.IsNullOrEmpty(me) && _gh.IsAuthenticated)
            foreach (var r in await _gh.ListReposAsync())
                repos.Add(new RepoInput(r.Name, r.Owner?.Login));

        // Aceptaciones registradas en BD.
        var acceptedIds = new List<long>();
        if (!string.IsNullOrEmpty(me))
            foreach (var a in await _sb.GetAcceptancesAsync(me))
                acceptedIds.Add(a.AssignmentId);

        // Entregas formales registradas en BD.
        var submissions = new List<SubmissionInput>();
        if (!string.IsNullOrEmpty(me))
            foreach (var s in await _sb.GetSubmissionsAsync(me))
                submissions.Add(new SubmissionInput(s.AssignmentId, s.RepoUrl, s.SubmittedAt));

        // Proyeccion de las invitaciones a la entrada pura, preservando null (no
        // se pudo consultar) vs lista vacia. invById permite reconstruir luego
        // unassociatedInvitations con los RepoInvitation originales.
        List<InvitationInput>? invInputs = null;
        var invById = new Dictionary<long, RepoInvitation>();
        if (invitations != null)
        {
            invInputs = new List<InvitationInput>(invitations.Count);
            foreach (var inv in invitations)
            {
                invInputs.Add(new InvitationInput(inv.Id, inv.Repository?.Name ?? "", inv.Inviter?.Login));
                invById[inv.Id] = inv;
            }
        }

        // ===== Calculo PURO: 5 senales -> estados + asociacion de invitaciones =====
        // RosterMatchConfirmed()/CurrentEvaluationOrg() se evaluan una sola vez
        // (son estables durante el calculo) y se pasan como datos al core.
        var calc = AssignmentStatusCalculator.Compute(
            asg.Select(a => new AssignmentInput(a.Id, a.Title, a.Section, a.Org)).ToList(),
            repos,
            acceptedIds,
            submissions,
            invInputs,
            me,
            RosterMatchConfirmed(),
            CurrentEvaluationOrg());

        // ===== Mapeo de vuelta al view-model WPF (mismo orden que asg) =====
        var byId = new Dictionary<long, Assignment>();
        foreach (var a in asg) byId[a.Id] = a;

        var result = new List<AssignmentStatus>(calc.Statuses.Count);
        foreach (var s in calc.Statuses)
        {
            result.Add(new AssignmentStatus
            {
                Assignment = byId[s.AssignmentId],
                Accepted = s.Accepted,
                RepoName = s.RepoName,
                RepoUrl = s.RepoUrl,
                Submitted = s.Submitted,
                SubmittedRepoUrl = s.SubmittedRepoUrl,
                SubmittedAt = s.SubmittedAt,
                InvitationId = s.InvitationId,
                InvitationPending = s.InvitationPending
            });
        }

        // Invitaciones vivas sin asociar: reconstruir los RepoInvitation originales.
        foreach (var u in calc.Unassociated)
            if (invById.TryGetValue(u.Id, out var inv))
                unassociatedInvitations.Add(inv);

        return result;
    }

    private async Task UpdateAssignmentsBanner()
    {
        // Confirmar matricula contra el roster ANTES de calcular (el resultado
        // endurece EXPECTED solo con match; ComputeAssignmentStatusesAsync lo
        // consulta via RosterMatchConfirmed). Es aditivo y nunca bloquea.
        await RefreshEnrollmentAsync();

        var asg = await _submission.GetSectionAssignmentsAsync();

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
        // El algebra vive en EntregaEvaluacion.Core AssignmentStatusCalculator.ToBuckets.
        var buckets = AssignmentStatusCalculator.ToBuckets(
            statuses.Select(s => (s.InvitationPending, s.Accepted, s.Submitted)));
        var pendienteAceptar = buckets.PendienteAceptar;
        var esperandoInvite = buckets.EsperandoInvite;
        var pendienteEntregar = buckets.PendienteEntregar;

        // Pendientes accionables: equivalente exacto, en el algebra de 5 senales,
        // del antiguo `pending = !Accepted && !Submitted` de roster-client. Manda
        // la visibilidad del link "Aceptar tareas" (que abre el dialogo). Las
        // invitaciones sin asociar son SOLO informativas y no cuentan aqui.
        var pendingActionable = buckets.PendingActionable;

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
        var asg = await _submission.GetSectionAssignmentsAsync();
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

        await _submission.RecordSubmissionAsync(status.Assignment.Id, me, input.Trim());
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
        await SyncExamTimeAsync();
        await RefreshBlocklistAsync();
        await UpdateAssignmentsBanner();
        await SendHeartbeatAsync();
        await _lockdown.CheckTargetedAsync();
        await CheckUpdateRequestAsync();
        await CheckNetworkProbeAsync();
    }

    /// <summary>
    /// Servicio del countdown anti-tamper, expuesto para que el widget de tiempo
    /// (ENT-31 slice 4) lea <c>ExamTimer.Remaining</c>. Esta slice solo siembra el
    /// ancla; no agrega UI.
    /// </summary>
    internal ExamTimerService ExamTimer => _examTimer;

    /// <summary>
    /// Re-ancla el countdown del examen con la hora autoritativa del servidor.
    /// Gate inExam: solo sincroniza cuando hay una evaluacion en curso
    /// (EvaluationId > 0). Si la RPC degrada (null: no desplegada / sin evaluacion
    /// / error), NO se re-ancla: el ExamTimerService conserva el ultimo ancla bueno
    /// y su Stopwatch monotonico sigue contando (un blip NUNCA congela ni reinicia
    /// el countdown). El sync captura el timestamp monotonico internamente, asi que
    /// se llama apenas vuelve el await. Solo se parsean los timestamps absolutos a
    /// DateTimeOffset (no es el reloj de pared del transcurrido: eso lo mide el
    /// Stopwatch dentro del servicio). NUNCA lanza dentro del tick.
    /// </summary>
    private async Task SyncExamTimeAsync()
    {
        if (_selection.EvaluationId is not { } evalId || evalId <= 0) return;

        var t = await _sb.GetExamTimeAsync(evalId);
        if (t == null) return; // degradar: conservar el ancla anterior.

        const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        if (!DateTimeOffset.TryParse(t.ServerNow, CultureInfo.InvariantCulture, styles, out var serverNow))
            return; // sin hora de servidor valida no hay ancla utilizable.

        DateTimeOffset? endsAt =
            DateTimeOffset.TryParse(t.EndsAt, CultureInfo.InvariantCulture, styles, out var ends)
                ? ends
                : null;

        _examTimer.Sync(serverNow, endsAt);
    }

    // Sonda de red (deteccion de contacto a Copilot): delegada al
    // NetworkProbeReporter (ENT-7 extraction #3), duenio del throttle de 30s y la
    // dedup de 5 min por host+source (antes _lastNetProbeUtc/_reportedAiHits vivian
    // aca). Es EVIDENCIA para revision, no veredicto. El guard de sesion
    // (_user == null) queda aca: sin sesion no se corre la sonda ni se toca el
    // throttle, igual que el original. now = DateTime.UtcNow se pasa una sola vez
    // por tick (el gating es determinista en el core).
    private async Task CheckNetworkProbeAsync()
    {
        if (_user == null) return;
        await _networkProbe.CheckAsync(
            _user.Login, _user.Email, Environment.MachineName,
            _selection.SectionText, _selection.SectionId, DateTime.UtcNow);
    }

    /// <summary>
    /// Update DISPARADO POR EL PROFE desde el panel (NO automatico): delegado al
    /// RemoteUpdateWatcher (ENT-7 extraction #3), duenio del arranque del cliente
    /// (_processStartUtc) y del one-shot dedup (_lastUpdateRequestProcessed), antes
    /// aca. El watcher lee el control, decide (request POSTERIOR al arranque y no
    /// procesado) y dispara el update UNA vez. La lectura de control ya se hace
    /// cada tick (Supabase, barato); GitHub solo se toca al disparar.
    /// </summary>
    private Task CheckUpdateRequestAsync() => _remoteUpdate.CheckAsync();

    /// <summary>
    /// Refresca el blocklist efectivo de la seccion del alumno. Si el fetch
    /// falla, GetBlocklistAsync devuelve null y dejamos _blocklist en null
    /// (=> IsSuspicious cae a Config.SuspiciousProcesses). Como AdminTickAsync
    /// corre en el arranque (antes del primer SendHeartbeatAsync), la primera
    /// deteccion ya usa la lista fetcheada si la red responde.
    /// </summary>
    private async Task RefreshBlocklistAsync()
    {
        // Delega el fetch al BlocklistRefresher (section_id preferido, cae a
        // section TEXT). null preservado = fallback a Config. El resultado se
        // guarda en _blocklist, que SendHeartbeatAsync pasa al armado de procesos.
        _blocklist = await _blocklistRefresher.RefreshAsync(
            _selection.SectionText, _selection.SectionId);
    }

    /// <summary>
    /// Polling del control admin: delega al LockdownCoordinator la aplicacion del
    /// control efectivo (internet/Copilot + pantalla roja remota) y conserva aca
    /// SOLO el concern del mensaje del profesor (dedup + MessageBox), que NO es
    /// lockdown. Degrade-closed: si el coordinador reporta que el control no se
    /// pudo resolver, return temprano sin tocar _lastAdminMessage (paridad exacta
    /// con el `if (cfg == null) return;` original).
    /// </summary>
    private async Task CheckAdminConfigAsync()
    {
        var result = await _lockdown.ApplyControlAsync();
        if (!result.ControlPresent) return;

        var msg = result.Message;
        if (!string.IsNullOrEmpty(msg) && msg != _lastAdminMessage)
        {
            _lastAdminMessage = msg;
            MessageBox.Show(msg, "Mensaje del profesor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        if (string.IsNullOrEmpty(msg)) _lastAdminMessage = "";
    }

    /// <summary>
    /// Implementacion del seam <see cref="IRedScreenHost"/>: construye el
    /// CheatWindow en el hilo de UI y lo muestra modal (ShowDialog), bloqueando
    /// hasta que se cierra. Es la UNICA frontera WPF de la pantalla roja; el
    /// coordinador decide CUANDO y CON QUE. SetOwner respeta la diferencia por-ruta
    /// del original (la trampa local fijaba Owner=this; la remota y la dirigida no).
    /// El retorno de ShowDialog no se consume (igual que el original).
    /// </summary>
    bool IRedScreenHost.ShowBlocking(RedScreenRequest req)
    {
        // ENT-31 slice 4: la pantalla roja SIEMPRE gana. ShowDialog solo DESHABILITA
        // las demas ventanas (no las oculta) y el Topmost del widget competiria en
        // z-order, asi que lo ocultamos EXPLICITAMENTE antes del modal. Corre en el
        // hilo de UI (igual que ShowBlocking), por lo que Hide() es seguro aqui.
        _widget?.Hide();

        var alert = new CheatWindow(
            req.RepoName, req.FilesCount, req.FilesNames,
            isPersistent: req.IsPersistent,
            remoteSource: req.RemoteSource,
            checkStillLocked: req.CheckStillLocked,
            onHeartbeat: req.OnHeartbeat);
        // La pantalla roja SIEMPRE debe llegar al frente, incluso con la app
        // OCULTA en la bandeja. Un Owner oculto (no visible) puede impedir que el
        // modal se active, asi que solo fijamos Owner cuando esta ventana esta
        // visible; si esta en la bandeja, el CheatWindow va sin Owner y su
        // Topmost+Activate (en CheatWindow_Loaded) lo lleva al frente igual. Nunca
        // se debilita el bloqueo: solo se evita un Owner que lo dejaria atras.
        if (req.SetOwner && IsVisible) alert.Owner = this;
        var result = alert.ShowDialog() == true;

        // Tras cerrarse la pantalla roja, re-evaluar la visibilidad: el widget solo
        // vuelve si seguimos minimizados, en evaluacion y sin lockdown (lo decide el
        // gate). El retorno de ShowDialog se preserva EXACTO (igual que el original).
        UpdateWidgetVisibility();
        return result;
    }

    private async Task SendHeartbeatAsync()
    {
        if (_user == null) return;
        // Tipo totalmente calificado: WPF-UI introduce otro ProcessInfo via
        // sus global usings, asi que apuntamos al DTO propio sin ambiguedad.
        List<EntregaEvaluacion.Models.ProcessInfo> procs = ProcessMonitor.GetOpenWindows();

        // Estados derivados: internet desde el servicio; lockdown desde el
        // coordinador, que ahora es el duenio de los flags (remoto/dirigido). Se
        // pasan ya resueltos como strings al HeartbeatReporter, que nunca los lee.
        var internetState = InternetBlockService.IsBlocked() ? "blocked" : "free";
        var lockdownState = _lockdown.IsLockdownActive ? "active" : "none";

        // El HeartbeatReporter detecta procesos nuevos sospechosos (set-diff puro),
        // alerta cada uno UNA vez y envia el heartbeat. Atribuye la presencia a la
        // evaluacion actual del alumno. El ON CONFLICT de online_clients NO cambia
        // en este slice (sigue pc_name+github_username); el aislamiento de
        // re-rendiciones por evaluacion es PR5.
        await _heartbeat.SendAsync(
            Environment.MachineName, _user.Login, _user.Email, _selection.SectionText,
            procs, _blocklist, internetState, lockdownState,
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

        await _lockdown.ShowLocalTrapAsync(reason, 0, new[] { reason });
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
