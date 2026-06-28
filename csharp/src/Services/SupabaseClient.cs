using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Cliente REST de Supabase. Anon key safe-to-share; escrituras protegidas
/// por RLS y/o RPC SECURITY DEFINER.
/// </summary>
public class SupabaseClient : ISupabaseClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // ===== Fail-safe del lockdown (degradar CERRADO) =====
    // Ultimo estado de force_lockdown EFECTIVO conocido (override ?? global)
    // para la evaluacion actual del cliente. null = nunca se resolvio.
    // Si una resolucion/fetch falla (red/null), RETENEMOS este valor en vez de
    // soltar a un alumno bloqueado por un parpadeo de red. Solo el primer arranque
    // sin estado previo (cache vacio) cae al default global (false). Asi un corte
    // de red transitorio NUNCA puede liberar a un alumno en medio del examen.
    private bool? _lastKnownLock;

    // Mismo fail-safe CERRADO para el lockdown DIRIGIDO (targeted_lockdowns).
    // null = nunca se resolvio. Distingue "query OK, sin fila activa" (=> false,
    // siembra cache) de "fetch fallido" (=> retener cache). Un blip de red en un
    // lock dirigido NUNCA libera al alumno.
    private bool? _lastKnownTargeted;

    // ===== Identidad verificada (JWT de enroll-identity) =====
    // El alumno arranca usando el anon key crudo como Bearer (comportamiento
    // historico). Tras un login exitoso de GitHub se intercambia el token de
    // GitHub por un JWT HS256 (role=anon) que porta github_username/github_id
    // VERIFICADOS. Desde entonces, TODAS las llamadas REST/RPC viajan con
    // Authorization: Bearer <jwt> (apikey sigue siendo el anon). Si no hay JWT
    // (cliente recien arrancado o enrolado fallido) se cae a anon crudo, asi el
    // flujo nunca se rompe (BACKWARD-COMPAT, FASE 1).
    private readonly string _anonKey = Config.SupabaseAnonKey;

    // Serializa la escritura del header Authorization default (puede ocurrir
    // concurrente con polls en vuelo) y protege _identityToken/_identityExpEpoch.
    private readonly object _identityLock = new();
    private string? _identityToken;
    private long _identityExpEpoch; // exp (epoch segundos) del JWT; 0 = sin identidad

    // Proveedor opcional del token de GitHub vigente, para re-enrolar la
    // identidad automaticamente cuando el JWT esta por expirar. Sin proveedor,
    // el cliente solo se enrola cuando el caller invoca EnrollIdentityAsync.
    private Func<string?>? _githubTokenProvider;

    public SupabaseClient()
    {
        // CRITICO: UseProxy=false. Sin esto, cuando bloqueamos internet (proxy
        // 127.0.0.1:1), el propio cliente perderia conexion a Supabase y nunca
        // recibiria la orden de desbloquear (catch-22). Ignorar el proxy del
        // sistema garantiza comunicacion con el backend siempre.
        var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Add("apikey", Config.SupabaseAnonKey);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Config.SupabaseAnonKey);
    }

    private string Rest(string path) => $"{Config.SupabaseUrl}/rest/v1/{path}";

    /// <summary>
    /// Helper generico para los GET de LISTA: trae el JSON de Rest(path) y lo
    /// deserializa a List&lt;T&gt;. En CUALQUIER error de red/parseo devuelve lista
    /// vacia (mismo fallback que tenian los metodos individuales: el caller cae a
    /// Config.* segun corresponda). NO usar para Blocklist/Allowlist, que devuelven
    /// null en error a proposito (semantica de fallback distinta).
    /// </summary>
    private async Task<List<T>> GetListAsync<T>(string path)
    {
        try { return JsonSerializer.Deserialize<List<T>>(await _http.GetStringAsync(Rest(path)), JsonOpts) ?? new(); }
        catch { return new(); }
    }

    // ===== Identidad verificada =====

    /// <summary>
    /// Registra un proveedor del token de GitHub vigente para que el cliente
    /// pueda re-enrolar la identidad por su cuenta cuando el JWT esta por
    /// expirar (ver EnsureIdentityFreshAsync). Es opcional: sin proveedor, la
    /// identidad solo se establece cuando el caller invoca EnrollIdentityAsync.
    /// </summary>
    public void SetGitHubTokenProvider(Func<string?>? provider) => _githubTokenProvider = provider;

    /// <summary>
    /// Porta (o limpia con null) el JWT de identidad verificada. Mientras haya
    /// token, TODAS las llamadas REST/RPC usan Authorization: Bearer &lt;jwt&gt;
    /// (apikey sigue siendo el anon). Con null, vuelve al anon key crudo como
    /// Bearer (backward-compat).
    ///
    /// CONCURRENCIA: la escritura del header default puede coincidir con
    /// peticiones en vuelo, pero solo reemplaza el VALOR de Authorization (no
    /// agrega/quita headers), de modo que cada request siguiente toma el token
    /// nuevo o el viejo de forma atomica. El lock serializa multiples
    /// SetIdentityToken entre si (login + refresh + logout). Es el mismo patron
    /// que GitHubService usa sobre sus DefaultRequestHeaders.
    /// </summary>
    public void SetIdentityToken(string? token)
    {
        lock (_identityLock)
        {
            _identityToken = string.IsNullOrWhiteSpace(token) ? null : token;
            if (_identityToken == null) _identityExpEpoch = 0;
            var bearer = _identityToken ?? _anonKey;
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearer);
        }
    }

    /// <summary>
    /// Intercambia el token de GitHub del alumno por un JWT de identidad firmado
    /// por la Edge Function enroll-identity. La function verifica el token contra
    /// api.github.com y emite un JWT HS256 con role=anon + github_username/
    /// github_id. En exito guarda el JWT (SetIdentityToken) y su exp. En
    /// CUALQUIER fallo deja la identidad en null: el cliente cae a anon crudo y
    /// el flujo no se rompe. Devuelve true si quedo enrolado.
    ///
    /// La llamada a la function se autentica SIEMPRE con el anon key (apikey +
    /// Authorization = anon), incluso si ya hay un identity JWT portado: forzamos
    /// el Bearer anon en el request para no encadenar identidades.
    /// </summary>
    public async Task<bool> EnrollIdentityAsync(string githubToken)
    {
        if (string.IsNullOrWhiteSpace(githubToken)) return false;
        try
        {
            var payload = JsonSerializer.Serialize(new { github_token = githubToken }, JsonOpts);
            // Slug real de la Edge Function en Supabase. Se deployo bajo el slug
            // por defecto "rapid-task" (el slug no es renombrable); el codigo es
            // el de enroll-identity. Si se recrea con slug "enroll-identity",
            // cambiar aca.
            using var req = new HttpRequestMessage(
                HttpMethod.Post, $"{Config.SupabaseUrl}/functions/v1/rapid-task")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            // apikey default ya es anon (nunca cambia). Solo forzamos el Bearer
            // anon en el request para sobrescribir un posible identity JWT default.
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _anonKey);

            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                SetIdentityToken(null);
                return false;
            }

            var json = await resp.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<EnrollIdentityResponse>(json, JsonOpts);
            if (data?.Token is { Length: > 0 } jwt)
            {
                SetIdentityToken(jwt);
                lock (_identityLock) { _identityExpEpoch = data.Exp; }
                return true;
            }

            // 200 sin token utilizable: tratar como fallo (cae a anon).
            SetIdentityToken(null);
            return false;
        }
        catch
        {
            SetIdentityToken(null);
            return false;
        }
    }

    /// <summary>
    /// True si hay identidad portada y el JWT vence dentro de los proximos 2 min
    /// (o ya vencio). Usado para re-enrolar proactivamente antes de un 401.
    /// </summary>
    private bool IdentityExpiringSoon()
    {
        lock (_identityLock)
        {
            if (_identityToken == null || _identityExpEpoch <= 0) return false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return _identityExpEpoch - now <= 120;
        }
    }

    /// <summary>
    /// Re-enrola la identidad si el JWT vigente esta por expirar, usando el
    /// proveedor de token de GitHub registrado. Best-effort y barato: no hace
    /// nada si no hay identidad, si todavia no vence pronto, o si no hay
    /// proveedor/token. Pensado para llamarse en el poll periodico del cliente.
    /// El enrolado inicial NO ocurre aca (lo dispara el caller tras el login).
    /// </summary>
    public async Task EnsureIdentityFreshAsync()
    {
        if (!IdentityExpiringSoon()) return;
        var gh = _githubTokenProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(gh)) return;
        await EnrollIdentityAsync(gh);
    }

    /// <summary>Respuesta de la Edge Function enroll-identity.</summary>
    private sealed class EnrollIdentityResponse
    {
        public string? Token { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("github_username")]
        public string? GithubUsername { get; set; }

        public long Exp { get; set; }
    }

    // ===== Control =====
    /// <summary>
    /// Override por PC (desbloqueo de internet/pantalla por nombre de equipo, sin
    /// depender del usuario). null si no hay fila o si fallo el fetch (fail-safe:
    /// sin override no se libera nada).
    /// </summary>
    public async Task<PcOverride?> GetPcOverrideAsync(string pcName)
    {
        try
        {
            var pc = Uri.EscapeDataString(pcName);
            var json = await _http.GetStringAsync(
                Rest($"pc_overrides?pc_name=eq.{pc}&select=pc_name,unblock_internet,unblock_screen"));
            var arr = JsonSerializer.Deserialize<PcOverride[]>(json, JsonOpts);
            return arr is { Length: > 0 } ? arr[0] : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// True solo si el profe puso unblock_screen=true para ESTE PC. Fail-safe:
    /// error/red caida => false (la pantalla roja NO se libera por un blip).
    /// </summary>
    public async Task<bool> IsPcScreenUnblockedAsync(string pcName)
        => (await GetPcOverrideAsync(pcName))?.UnblockScreen ?? false;

    public async Task<ControlState?> GetControlAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(Rest("control?id=eq.1&select=*"));
            var arr = JsonSerializer.Deserialize<ControlState[]>(json, JsonOpts);
            return arr is { Length: > 0 } ? arr[0] : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Lee el override de control de una evaluacion (tabla evaluation_control)
    /// distinguiendo tres casos:
    ///   - fila encontrada  => (true, fila)
    ///   - sin fila (la evaluacion hereda el global) => (true, null)
    ///   - FETCH FALLIDO (red/parseo)               => (false, null)
    /// El tercer flag (ok=false) permite al resolver degradar CERRADO en vez de
    /// confundir "no hay override" con "no pude leer el override". Los campos de
    /// la fila son nullables: NULL en un campo = heredar el global para ESE campo.
    /// </summary>
    public async Task<(bool ok, EvaluationControl? row)> GetEvaluationControlAsync(long evaluationId)
    {
        try
        {
            var json = await _http.GetStringAsync(
                Rest($"evaluation_control?evaluation_id=eq.{evaluationId}&select=*"));
            var arr = JsonSerializer.Deserialize<EvaluationControl[]>(json, JsonOpts);
            return (true, arr is { Length: > 0 } ? arr[0] : null);
        }
        catch { return (false, null); }
    }

    /// <summary>
    /// Resuelve el control EFECTIVO de la evaluacion actual del cliente:
    /// override por evaluacion (si existe la fila) ELSE control global id=1,
    /// resolviendo campo a campo como (override.campo ?? global.campo).
    ///
    /// REGLA DE RESOLUCION (la misma en todos los call sites):
    ///   EFFECTIVE = per-eval override (si hay fila) ELSE global id=1.
    ///   Cada campo individual: override.campo ?? global.campo.
    /// Orden de resolucion concreto:
    ///   1. override row existe                 -> usar override (?? global por campo)
    ///   2. override query OK pero vacia        -> usar global
    ///   3. override query FALLA                -> intentar global; si global OK -> global
    ///                                             (asi el control GLOBAL sigue vivo aunque
    ///                                              evaluation_control no este desplegada/falle)
    ///   4. override FALLA y global FALLA        -> null (degradar CERRADO)
    ///
    /// FUENTE UNICA DEL CACHE: cada vez que esto RESUELVE un ControlState
    /// no-null (override o global), siembra _lastKnownLock con el
    /// force_lockdown resuelto. Asi TANTO la ruta de APERTURA
    /// (CheckAdminConfigAsync) COMO la de LIBERACION (IsForceLockdownAsync)
    /// calientan/leen el MISMO cache, y un lock recien aplicado por la apertura
    /// queda cacheado antes del primer poll de liberacion.
    ///
    /// FAIL-SAFE (degradar CERRADO): solo devuelve null cuando NO puede resolver
    /// el control con certeza (ambos fetch fallan). Un null aqui NUNCA debe
    /// interpretarse como "desbloquear": el caller retiene su ultimo conocido.
    ///
    /// evaluationId == null (alumno sin evaluacion elegida): no hay override
    /// posible, se usa el global directamente (null si el global fallo).
    /// </summary>
    public async Task<ControlState?> GetEffectiveControlAsync(long? evaluationId)
    {
        if (evaluationId is not { } evalId)
        {
            // Sin evaluacion elegida => control global tal cual (o null si fallo).
            var g0 = await GetControlAsync();
            if (g0 != null) _lastKnownLock = g0.ForceLockdown;
            return g0;
        }

        var (ok, ovr) = await GetEvaluationControlAsync(evalId);

        if (!ok)
        {
            // FIX 3: el fetch del override fallo. NO degradamos CERRADO de una:
            // primero intentamos el control global, asi el alumno NO pierde el
            // bloqueo/internet_block global cuando evaluation_control no esta
            // desplegada o tuvo un blip. Solo si el global TAMBIEN falla
            // devolvemos null (degradar CERRADO; el caller retiene su cache).
            var gFallback = await GetControlAsync();
            if (gFallback != null) _lastKnownLock = gFallback.ForceLockdown;
            return gFallback;
        }

        var global = await GetControlAsync();

        // Sin override: el control efectivo es el global (null si el global fallo
        // => el caller degrada CERRADO reteniendo el ultimo conocido).
        if (ovr == null)
        {
            if (global != null) _lastKnownLock = global.ForceLockdown;
            return global;
        }

        // Con override: si el override NO fija force_lockdown (lo deja NULL) y el
        // global no respondio, no puedo resolver el lock efectivo => null
        // (degradar CERRADO). Si el override fija force_lockdown explicitamente,
        // ese valor manda aunque el global no responda.
        if (ovr.ForceLockdown == null && global == null) return null;

        // Resolucion campo a campo: override.campo ?? global.campo.
        var effective = new ControlState
        {
            InternetBlock = ovr.InternetBlock ?? global?.InternetBlock ?? false,
            ForceLockdown = ovr.ForceLockdown ?? global?.ForceLockdown ?? false,
            Message = ovr.Message ?? global?.Message,
            UpdatedAt = ovr.UpdatedAt ?? global?.UpdatedAt,
            UpdatedBy = ovr.UpdatedBy ?? global?.UpdatedBy
        };
        _lastKnownLock = effective.ForceLockdown;
        return effective;
    }

    // ===== Multi-evaluacion: cursos, secciones, evaluaciones =====
    // Fallback: si la BD no responde, el caller cae a Config.Sections/EvaluationTypes.

    public async Task<List<Course>> GetCoursesAsync()
    {
        return await GetListAsync<Course>("courses?active=eq.true&select=*&order=code.asc");
    }

    public async Task<List<SectionRow>> GetSectionsAsync(long? courseId = null)
    {
        // PostgREST espera el filtro DESPUES del nombre de tabla:
        //   sections?course_id=eq.123&select=*
        // Antes se generaba "course_id=eq.123&sections?select=*" (URL invalida).
        var filter = courseId is { } cid ? $"?course_id=eq.{cid}&select=*&order=code.asc"
                                         : "?select=*&order=code.asc";
        return await GetListAsync<SectionRow>($"sections{filter}");
    }

    public async Task<List<Evaluation>> GetEvaluationsAsync(long sectionId, bool onlyActive = true)
    {
        var activeFilter = onlyActive ? "&active=eq.true" : "";
        return await GetListAsync<Evaluation>(
            $"evaluations?section_id=eq.{sectionId}{activeFilter}&select=*&order=created_at.desc");
    }

    // ===== Assignments =====
    /// <summary>
    /// Trae assignments activos. Si se pasa evaluationId, filtra por
    /// evaluation_id=eq.X (cuando el alumno ya eligio una evaluacion
    /// concreta). Sin filtro, trae todos los activos (comportamiento
    /// legacy para coexistencia con clientes viejos).
    /// </summary>
    public async Task<List<Assignment>> GetActiveAssignmentsAsync(long? evaluationId = null)
    {
        var evalFilter = evaluationId is { } id ? $"evaluation_id=eq.{id}&" : "";
        return await GetListAsync<Assignment>(
            $"assignments?{evalFilter}active=eq.true&select=*&order=created_at.desc");
    }

    // ===== Blocklist de procesos sospechosos =====

    /// <summary>
    /// Devuelve la lista efectiva de procesos sospechosos para una seccion:
    /// reglas globales (section IS NULL) union reglas de la seccion dada.
    /// Soporta tanto section TEXT (legacy) como section_id (multi-evaluacion).
    /// Si sectionId viene != null, filtra por section_id; si no, cae a section
    /// TEXT (forward-compat con clientes viejos). Cada process_name se
    /// normaliza (paridad con la deteccion) y se arma un HashSet.
    ///
    /// Devuelve null en CUALQUIER error de red/parseo => el caller cae al
    /// fallback (Config.SuspiciousProcesses). NO devuelve set vacio en error:
    /// null y [] significan cosas distintas (null=fallo/usar fallback,
    /// []=tabla vacia que el detector tambien trata como fallback).
    /// </summary>
    public async Task<HashSet<string>?> GetBlocklistAsync(string? section, long? sectionId = null)
    {
        try
        {
            // section.is.null cubre las reglas globales; si hay seccion se suma
            // section.eq.X (o section_id.eq.Y cuando se usa multi-evaluacion).
            // PostgREST OR: or=(section.is.null,section.eq.X).
            // El valor va entre comillas dobles para que PostgREST lo trate
            // como literal. Se url-encodea igual.
            string filter;
            if (sectionId is { } sid)
            {
                // Multi-evaluacion: filtrar por section_id (preferido) ya que
                // section TEXT puede ser NULL en filas migradas.
                filter = $"or=(section.is.null,section_id.eq.{sid})";
            }
            else if (string.IsNullOrWhiteSpace(section))
            {
                filter = "section=is.null";
            }
            else
            {
                var quoted = Uri.EscapeDataString($"\"{section}\"");
                filter = $"or=(section.is.null,section.eq.{quoted})";
            }

            var json = await _http.GetStringAsync(
                Rest($"suspicious_processes?{filter}&select=process_name"));
            var rows = JsonSerializer.Deserialize<List<SuspiciousProcess>>(json, JsonOpts);
            if (rows == null) return null;

            var set = new HashSet<string>();
            foreach (var r in rows)
            {
                var norm = Config.NormalizeProcessName(r.ProcessName);
                if (norm.Length > 0) set.Add(norm);
            }
            return set;
        }
        catch { return null; }
    }

    /// <summary>
    /// Allowlist efectiva (global UNION seccion) de la tabla allowed_urls,
    /// para el navegador embebido. Espejo de GetBlocklistAsync. Devuelve null
    /// en fallo de red O tabla vacia: ambos casos significan "usar el fallback
    /// hardcodeado de Config" (AllowedBrowseDomains + AllowedExactUrls). Un
    /// fetch fallido jamas amplia NI restringe a cero lo permitido.
    /// </summary>
    public async Task<List<AllowedUrl>?> GetAllowlistAsync(string? section, long? sectionId = null)
    {
        try
        {
            string filter;
            if (sectionId is { } sid)
                filter = $"or=(section.is.null,section_id.eq.{sid})";
            else if (string.IsNullOrWhiteSpace(section))
                filter = "section=is.null";
            else
            {
                var quoted = Uri.EscapeDataString($"\"{section}\"");
                filter = $"or=(section.is.null,section.eq.{quoted})";
            }

            var json = await _http.GetStringAsync(
                Rest($"allowed_urls?{filter}&select=pattern,kind"));
            var rows = JsonSerializer.Deserialize<List<AllowedUrl>>(json, JsonOpts);
            // null (error de shape) o tabla vacia => usar fallback de Config.
            if (rows == null || rows.Count == 0) return null;
            return rows;
        }
        catch { return null; }
    }

    // ===== Aceptaciones de tareas =====

    /// <summary>
    /// Registra (upsert) que un alumno acepto una tarea de Classroom via RPC
    /// SECURITY DEFINER. Mismo patron silencioso que SendHeartbeatAsync.
    /// </summary>
    public async Task RecordAcceptanceAsync(
        string githubUsername, long assignmentId, string? title,
        string? section, string? repoName, string? repoUrl,
        long? evaluationId = null)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                p_github_username = githubUsername,
                p_assignment_id = assignmentId,
                p_assignment_title = title,
                p_section = section,
                p_repo_name = repoName,
                p_repo_url = repoUrl,
                p_evaluation_id = evaluationId
            }, JsonOpts);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _http.PostAsync(Rest("rpc/record_acceptance"), content);
        }
        catch { }
    }

    /// <summary>
    /// Devuelve las aceptaciones registradas de un alumno. Lista vacia si falla.
    /// </summary>
    public async Task<List<Acceptance>> GetAcceptancesAsync(string githubUsername)
    {
        var user = Uri.EscapeDataString(githubUsername);
        return await GetListAsync<Acceptance>(
            $"assignment_acceptances?github_username=eq.{user}&select=*");
    }

    /// <summary>
    /// Devuelve las entregas formales registradas de un alumno. Lista vacia si falla.
    /// </summary>
    public async Task<List<Submission>> GetSubmissionsAsync(string githubUsername)
    {
        var user = Uri.EscapeDataString(githubUsername);
        return await GetListAsync<Submission>(
            $"assignment_submissions?github_username=eq.{user}&select=*");
    }

    /// <summary>
    /// Registra (upsert) que un alumno entrego un repo via RPC SECURITY DEFINER.
    /// </summary>
    public async Task RecordSubmissionAsync(long assignmentId, string githubUsername, string repoUrl)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                p_assignment_id = assignmentId,
                p_github_username = githubUsername,
                p_repo_url = repoUrl,
                p_status = "submitted"
            }, JsonOpts);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _http.PostAsync(Rest("rpc/record_submission"), content);
        }
        catch { }
    }

    // ===== Roster: confirmacion de matricula (RPC SECURITY DEFINER) =====

    /// <summary>
    /// Confirma la matricula del alumno actual contra el roster (enrollments)
    /// via la RPC get_my_enrollment. Es el UNICO acceso del cliente anon a esa
    /// tabla: enrollments tiene RLS authenticated-only y la RPC (SECURITY
    /// DEFINER) devuelve SOLO campos de confirmacion no-PII (section_id, status,
    /// found). Nunca expone full_name/email/blackboard_student_id.
    ///
    /// Distingue tres resultados:
    ///   - Confirmed=true, Found=true  => match en el roster (matricula confirmada).
    ///   - Confirmed=true, Found=false => la RPC respondio sin match (no matriculado).
    ///   - Confirmed=false             => no se pudo confirmar (red/parseo fallo).
    ///
    /// CRITICO: en error de red/parseo devuelve el centinela CouldNotConfirm
    /// (Confirmed=false), NUNCA un found=false que se haga pasar por un "no
    /// matriculado" definitivo. El caller cae al comportamiento por defecto en
    /// ese caso (no endurece EXPECTED, no suprime entregas pendientes).
    /// Devuelve null solo cuando faltan datos para llamar (sin username/seccion).
    /// </summary>
    public async Task<MyEnrollment?> GetMyEnrollmentAsync(string githubUsername, long sectionId)
    {
        if (string.IsNullOrWhiteSpace(githubUsername)) return null;
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                p_github_username = githubUsername,
                p_section_id = sectionId
            }, JsonOpts);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(Rest("rpc/get_my_enrollment"), content);

            // Un status no-exitoso NO es un "no matriculado": no podemos
            // confirmar. Centinela could-not-confirm.
            if (!resp.IsSuccessStatusCode)
                return MyEnrollment.CouldNotConfirm;

            var json = await resp.Content.ReadAsStringAsync();
            var rows = JsonSerializer.Deserialize<List<MyEnrollmentRow>>(json, JsonOpts);

            // La RPC siempre devuelve al menos una fila (match o fila de
            // confirmacion negativa con found=false). Si el parseo no produjo
            // filas, no podemos confirmar (no asumimos "no matriculado").
            if (rows is not { Count: > 0 })
                return MyEnrollment.CouldNotConfirm;

            var row = rows[0];
            return new MyEnrollment
            {
                Confirmed = true,
                Found = row.Found,
                SectionId = row.SectionId,
                Status = row.Status
            };
        }
        catch
        {
            // Red/parseo fallo: NO es "no matriculado". Could-not-confirm.
            return MyEnrollment.CouldNotConfirm;
        }
    }

    // ===== Heartbeat (RPC SECURITY DEFINER) =====
    // section_id se sincroniza via trigger trg_sync_section_online desde
    // section TEXT; la RPC heartbeat no acepta p_section_id (forward-compat).
    // p_evaluation_id (nullable, default NULL en la RPC desde PR2) atribuye la
    // presencia a la evaluacion actual del alumno. NO se cambia el ON CONFLICT
    // de online_clients (sigue siendo pc_name+github_username): el swap a
    // COALESCE(evaluation_id,0) es PR5 (gate 4-antes-de-5), no este slice.
    public async Task SendHeartbeatAsync(
        string pcName, string githubUsername, string? githubEmail,
        string? section, List<ProcessInfo> processes,
        string internetState = "free", string lockdownState = "none",
        long? evaluationId = null, string? appVersion = null)
    {
        try
        {
            // p_app_version es nullable (DEFAULT NULL en la RPC) para que clientes
            // viejos que no lo mandan sigan resolviendo. Reporta la version que
            // corre el alumno -> el panel la muestra por PC.
            var payload = JsonSerializer.Serialize(new
            {
                p_pc_name = pcName,
                p_github_username = githubUsername,
                p_github_email = githubEmail,
                p_section = section,
                p_processes = processes,
                p_internet_state = internetState,
                p_lockdown_state = lockdownState,
                p_evaluation_id = evaluationId,
                p_app_version = appVersion
            }, JsonOpts);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _http.PostAsync(Rest("rpc/heartbeat"), content);
        }
        catch { }
    }

    // ===== Targeted lockdown =====
    /// <summary>
    /// Indica si hay un lockdown DIRIGIDO activo para este PC+usuario, con
    /// fail-safe CERRADO igual que el force_lockdown:
    ///   - query OK con fila activa  => true  (siembra _lastKnownTargeted)
    ///   - query OK sin fila activa  => false (siembra _lastKnownTargeted=false:
    ///                                          una liberacion genuina si libera)
    ///   - FETCH FALLIDO (blip)      => retiene _lastKnownTargeted ?? false
    /// Asi un parpadeo de red en un lock DIRIGIDO (aunque no haya force_lockdown
    /// global/por-eval activo) NUNCA libera al alumno: el catch retiene el ultimo
    /// estado conocido en vez de devolver false a ciegas.
    /// </summary>
    public async Task<bool> IsTargetedLockedAsync(string pcName, string githubUsername)
    {
        try
        {
            var pc = Uri.EscapeDataString(pcName);
            var user = Uri.EscapeDataString(githubUsername);
            var json = await _http.GetStringAsync(
                Rest($"targeted_lockdowns?pc_name=eq.{pc}&github_username=eq.{user}&active=eq.true&select=id,reason"));
            var arr = JsonSerializer.Deserialize<TargetedLockdown[]>(json, JsonOpts);
            var locked = arr is { Length: > 0 };
            // Query exitosa: este es el estado REAL (haya o no fila). Sembramos el
            // cache; una liberacion genuina (sin fila activa) si debe liberar.
            _lastKnownTargeted = locked;
            return locked;
        }
        catch
        {
            // Fetch fallido: degradar CERRADO reteniendo el ultimo conocido. Solo
            // el primer arranque sin estado previo cae a false.
            return _lastKnownTargeted ?? false;
        }
    }

    public async Task<string?> GetTargetedReasonAsync(string pcName, string githubUsername)
    {
        try
        {
            var pc = Uri.EscapeDataString(pcName);
            var user = Uri.EscapeDataString(githubUsername);
            var json = await _http.GetStringAsync(
                Rest($"targeted_lockdowns?pc_name=eq.{pc}&github_username=eq.{user}&active=eq.true&select=reason"));
            var arr = JsonSerializer.Deserialize<TargetedLockdown[]>(json, JsonOpts);
            return arr is { Length: > 0 } ? arr[0].Reason : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Reporta que ESTE PC+usuario quedo bloqueado por una trampa LOCAL, via RPC
    /// SECURITY DEFINER (anon no puede escribir targeted_lockdowns directo). Asi
    /// el profe lo VE en el panel y puede DESBLOQUEARLO remoto (la liberacion es
    /// authenticated-only: el alumno no se auto-libera). Devuelve true si el
    /// reporte se confirmo: el caller usa eso para decidir si la pantalla roja es
    /// liberable-remoto (reportada) o password-only (fallo => fail-safe, no auto-libera).
    /// </summary>
    public async Task<bool> ReportSelfLockAsync(
        string pcName, string githubUsername, string? section, string? reason)
    {
        if (string.IsNullOrWhiteSpace(githubUsername)) return false;
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                p_pc_name = pcName,
                p_github_username = githubUsername,
                p_section = section,
                p_reason = reason,
                p_source = "trap"
            }, JsonOpts);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(Rest("rpc/report_self_lock"), content);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Predicado de force_lockdown EFECTIVO para la evaluacion actual del
    /// cliente: resuelve override por evaluacion ?? global id=1. Es a la vez el
    /// predicado de APERTURA (CheckAdminConfigAsync) y de LIBERACION
    /// (checkStillLocked de CheatWindow), leyendo EXACTAMENTE la misma
    /// resolucion. GetEffectiveControlAsync es la fuente unica del cache
    /// _lastKnownLock: lo siembra en cada resolucion no-null (incluida la
    /// apertura). Por eso aca solo leemos: si la resolucion devuelve null
    /// (ambos fetch fallan) degradamos CERRADO reteniendo _lastKnownLock, asi un
    /// parpadeo de red NUNCA libera a un alumno bloqueado en medio del examen.
    /// Solo el primer arranque sin estado previo cae al default global (false).
    /// </summary>
    public async Task<bool> IsForceLockdownAsync(long? evaluationId)
    {
        var ctl = await GetEffectiveControlAsync(evaluationId);
        // ctl no-null: el resolver ya sembro _lastKnownLock con ctl.ForceLockdown.
        // ctl null (ambos fetch fallaron): fail-safe CERRADO reteniendo el cache.
        return ctl?.ForceLockdown ?? _lastKnownLock ?? false;
    }

    // ===== Reportes (INSERT directo, RLS anon insert) =====
    public async Task ReportCheatEventAsync(
        string username, string pcName, string repoName, int filesCount, string[] filesSample)
    {
        await PostInsertAsync("cheat_events", new
        {
            username, pc_name = pcName, repo_name = repoName,
            files_count = filesCount, files_sample = filesSample
        });
    }

    public async Task ReportStudentActivityAsync(
        string action, string githubUsername, string? githubEmail,
        string pcName, string? section, string? repoName, string? repoUrl,
        long? sectionId = null)
    {
        await PostInsertAsync("student_activity", new
        {
            action, github_username = githubUsername, github_email = githubEmail,
            pc_name = pcName, section, section_id = sectionId,
            repo_name = repoName, repo_url = repoUrl
        });
    }

    /// <summary>
    /// Reporta una alerta de proceso sospechoso via RPC SECURITY DEFINER
    /// (anti-flood server-side: rate-limit 30s por pc_name+process_name). El
    /// cliente ya no inserta directo en process_alerts. Patron y catch silencioso
    /// identicos a SendHeartbeatAsync. CRITICO: nombre y orden de los argumentos
    /// deben coincidir con la firma SQL report_process_alert(
    ///   p_github_username, p_pc_name, p_section, p_process_name, p_window_title).
    /// </summary>
    public async Task ReportProcessAlertAsync(
        string githubUsername, string pcName, string? section,
        string processName, string windowTitle)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                p_github_username = githubUsername,
                p_pc_name = pcName,
                p_section = section,
                p_process_name = processName,
                p_window_title = windowTitle
            }, JsonOpts);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(Rest("rpc/report_process_alert"), content);
            // La alerta es evidencia de proctoring: si la RPC responde error
            // (RLS/grant regresion, fallo del rate-limit, etc.) dejamos rastro
            // en Debug en vez de tragarlo del todo. Sigue siendo no-fatal para
            // el alumno (catch silencioso para la UI).
            if (!resp.IsSuccessStatusCode)
                System.Diagnostics.Debug.WriteLine(
                    $"[SupabaseClient] report_process_alert fallo: {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SupabaseClient] report_process_alert excepcion: {ex.Message}");
        }
    }

    /// <summary>
    /// Registra una navegacion del alumno en el navegador embebido. INSERT
    /// directo (RLS anon insert), silencioso. allowed=true cuando el dominio
    /// estaba en la whitelist; allowed=false cuando se bloqueo (trampa).
    /// </summary>
    public async Task ReportBrowsingAsync(
        string githubUsername, string pcName, string? section,
        string url, string domain, bool allowed,
        long? sectionId = null)
    {
        await PostInsertAsync("browser_history", new
        {
            github_username = githubUsername, pc_name = pcName, section,
            section_id = sectionId, url, domain, allowed
        });
    }

    private async Task PostInsertAsync(string table, object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOpts);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _http.PostAsync(Rest(table), content);
        }
        catch { }
    }
}
