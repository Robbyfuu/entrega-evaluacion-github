namespace EntregaEvaluacion.Core;

// ===== Entradas puras (sin WPF, sin I/O) =====
// El caller (MainWindow) hace los fetches y mapea sus DTOs (Assignment,
// GitHubRepo, Acceptance, Submission, RepoInvitation) a estos records antes de
// llamar al calculo. Asi el algebra de estados queda testeable en EntregaEvaluacion.Core
// sin arrastrar la capa de UI ni los DTOs del proyecto WPF.

/// <summary>Tarea esperada (proyeccion pura de Models.Assignment).</summary>
public sealed record AssignmentInput(long Id, string Title, string? Section, string? Org);

/// <summary>Repo del alumno (proyeccion pura de Models.GitHubRepo).</summary>
public sealed record RepoInput(string Name, string? OwnerLogin);

/// <summary>Entrega formal registrada en BD (proyeccion de Models.Submission).</summary>
public sealed record SubmissionInput(long AssignmentId, string? RepoUrl, string? SubmittedAt);

/// <summary>Invitacion de repo viva (proyeccion pura de Models.RepoInvitation).</summary>
public sealed record InvitationInput(long Id, string RepoName, string? InviterLogin);

/// <summary>
/// Estado calculado de una tarea (resultado puro). El caller lo mapea al
/// view-model WPF Models.AssignmentStatus para el banner y el dialogo. Mutable
/// en InvitationId/InvitationPending porque la asociacion invitacion->tarea se
/// resuelve en un segundo paso (longest-prefix) tras armar la lista base.
/// </summary>
public sealed class AssignmentStatusResult
{
    public long AssignmentId { get; init; }
    public bool Accepted { get; init; }
    public string? RepoName { get; init; }
    public string? RepoUrl { get; init; }
    public bool Submitted { get; init; }
    public string? SubmittedRepoUrl { get; init; }
    public string? SubmittedAt { get; init; }
    public long? InvitationId { get; set; }
    public bool InvitationPending { get; set; }
}

/// <summary>
/// Resultado del calculo: estados por tarea + invitaciones vivas que no
/// matchearon ninguna tarea esperada (informativas para el banner).
/// </summary>
public sealed record AssignmentCalculation(
    IReadOnlyList<AssignmentStatusResult> Statuses,
    IReadOnlyList<InvitationInput> Unassociated);

/// <summary>
/// Los 3 buckets DISJUNTOS del banner. Cada status suma a lo sumo a uno.
/// </summary>
public readonly record struct AssignmentBuckets(
    int PendienteAceptar, int EsperandoInvite, int PendienteEntregar)
{
    // Pendientes accionables: manda la visibilidad del link "Aceptar tareas".
    public int PendingActionable => PendienteAceptar + EsperandoInvite + PendienteEntregar;
}

/// <summary>
/// Algebra PURA de estados de tarea, extraida de
/// MainWindow.ComputeAssignmentStatusesAsync + UpdateAssignmentsBanner.
///
/// Determina, para cada tarea de la seccion, su estado segun las 5 senales:
/// OWNED (repo esperado existe), ACCEPTED_DB (assignment_acceptances),
/// SUBMITTED (assignment_submissions), INVITED (repository_invitations) y
/// EXPECTED (la propia lista). Las invitaciones se asocian por PREFIJO de slug
/// con desempate por inviter-org, delegando el matching al core compartido
/// ClassroomRepoMatcher.PickByLongestPrefix (misma logica que el banner y
/// AcceptInvitationsAsync). Sin I/O: el caller ya trae los datos.
/// </summary>
public static class AssignmentStatusCalculator
{
    public static AssignmentCalculation Compute(
        IReadOnlyList<AssignmentInput> assignments,
        IEnumerable<RepoInput> repos,
        IEnumerable<long> acceptedAssignmentIds,
        IEnumerable<SubmissionInput> submissions,
        IReadOnlyList<InvitationInput>? invitations,
        string? githubUsername,
        bool rosterMatchConfirmed,
        string? evaluationOrg)
    {
        var result = new List<AssignmentStatusResult>();
        var unassociated = new List<InvitationInput>();

        if (assignments.Count == 0)
        {
            // Sin tareas esperadas, toda invitacion viva queda sin asociar.
            if (invitations != null) unassociated.AddRange(invitations);
            return new AssignmentCalculation(result, unassociated);
        }

        var me = githubUsername;

        // Repos del alumno (para detectar el repo esperado de cada tarea).
        var repoNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reposByName = new Dictionary<string, RepoInput>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in repos)
        {
            repoNames.Add(r.Name);
            reposByName[r.Name] = r;
        }

        // Aceptaciones registradas en BD.
        var acceptedIds = new HashSet<long>(acceptedAssignmentIds);

        // Entregas formales registradas en BD.
        var submittedIds = new HashSet<long>();
        var submissionsByAssignment = new Dictionary<long, SubmissionInput>();
        foreach (var s in submissions)
        {
            submittedIds.Add(s.AssignmentId);
            submissionsByAssignment[s.AssignmentId] = s;
        }

        // Invitaciones vivas pendientes de consumir. Se van quitando a medida que
        // se asocian a una tarea, para luego reportar las sobrantes como "sin
        // asociar". null = no se pudo consultar (no es lista vacia).
        var remainingInvites = invitations != null
            ? new List<InvitationInput>(invitations)
            : new List<InvitationInput>();

        // Pares (input, status) para resolver la asociacion invitacion->tarea por
        // identidad de objeto, igual que el ReferenceEquals del codigo original.
        var pairs = new List<(AssignmentInput Input, AssignmentStatusResult Status)>();

        foreach (var a in assignments)
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
                    var owner = reposByName.TryGetValue(expected, out var r) && r.OwnerLogin != null
                        ? r.OwnerLogin : me;
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
            if (rosterMatchConfirmed
                && !hasRepo && !accepted && !submitted
                && string.IsNullOrEmpty(a.Section))
            {
                continue;
            }

            // INVITED: la asociacion invitacion<->tarea se resuelve mas abajo en
            // un paso aparte (longest-prefix-wins), porque procesar las tareas en
            // el orden de assignments permitiria que un slug corto ("tarea-") robe
            // la invitacion de uno mas especifico ("tarea-extra-"). Aqui solo se
            // arma el estado base; InvitationId/InvitationPending se rellenan luego.
            var status = new AssignmentStatusResult
            {
                AssignmentId = a.Id,
                Accepted = accepted,
                RepoName = repoName,
                RepoUrl = repoUrl,
                Submitted = submitted,
                SubmittedRepoUrl = sub?.RepoUrl,
                SubmittedAt = sub?.SubmittedAt
            };
            result.Add(status);
            pairs.Add((a, status));
        }

        // Asociacion determinista invitacion -> tarea con LONGEST-PREFIX-WINS:
        // se procesa cada invitacion contra las tareas aun sin invitacion,
        // delegando el algoritmo al core ClassroomRepoMatcher (mas especifico
        // primero, desempate por inviter-org). Cada invitacion se asigna a lo
        // sumo a una tarea; el orden de salida (result) se mantiene en el orden
        // original de assignments.
        foreach (var inv in remainingInvites.ToList())
        {
            var match = MatchAssignmentForRepo(pairs, inv, evaluationOrg);
            if (match == null) continue;
            match.InvitationId = inv.Id;
            match.InvitationPending = true;
            remainingInvites.Remove(inv);
        }

        // Invitaciones vivas que no matchearon ninguna tarea esperada.
        unassociated.AddRange(remainingInvites);
        return new AssignmentCalculation(result, unassociated);
    }

    /// <summary>
    /// Resuelve, para una invitacion de repo, la tarea (status) a la que
    /// pertenece usando LONGEST-PREFIX-WINS. Solo considera tareas aun sin
    /// invitacion asociada (InvitationPending=false) y delega el algoritmo al
    /// core ClassroomRepoMatcher.PickByLongestPrefix para no divergir del banner
    /// ni de AcceptInvitationsAsync.
    /// </summary>
    private static AssignmentStatusResult? MatchAssignmentForRepo(
        List<(AssignmentInput Input, AssignmentStatusResult Status)> pairs,
        InvitationInput inv,
        string? evaluationOrg)
    {
        var repoName = inv.RepoName ?? "";
        if (repoName.Length == 0) return null;

        // Solo tareas que aun no tienen invitacion: asi cada invitacion se asigna
        // a lo sumo a una tarea (matching bipartito).
        var unclaimed = pairs.Where(p => !p.Status.InvitationPending).ToList();
        var match = ClassroomRepoMatcher.PickByLongestPrefix(
            unclaimed.Select(p => p.Input),
            repoName, inv.InviterLogin, evaluationOrg,
            a => a.Title, a => a.Org);
        if (match == null) return null;
        return unclaimed
            .FirstOrDefault(p => ReferenceEquals(p.Input, match))
            .Status;
    }

    /// <summary>
    /// Proyecta las 5 senales por status a los 3 buckets DISJUNTOS del banner.
    /// Preserva EXACTO el algebra original:
    ///   pendienteAceptar  = INVITED ∧ ¬ACCEPTED
    ///   esperandoInvite   = ¬INVITED ∧ ¬ACCEPTED ∧ ¬SUBMITTED
    ///   pendienteEntregar = ACCEPTED ∧ ¬SUBMITTED
    /// </summary>
    public static AssignmentBuckets ToBuckets(
        IEnumerable<(bool invitationPending, bool accepted, bool submitted)> signals)
    {
        int pendienteAceptar = 0, esperandoInvite = 0, pendienteEntregar = 0;
        foreach (var (inv, acc, sub) in signals)
        {
            if (inv && !acc) pendienteAceptar++;
            if (!inv && !acc && !sub) esperandoInvite++;
            if (acc && !sub) pendienteEntregar++;
        }
        return new AssignmentBuckets(pendienteAceptar, esperandoInvite, pendienteEntregar);
    }

    /// <summary>Sobrecarga de conveniencia sobre los resultados ya calculados.</summary>
    public static AssignmentBuckets ToBuckets(IEnumerable<AssignmentStatusResult> statuses)
        => ToBuckets(statuses.Select(s => (s.InvitationPending, s.Accepted, s.Submitted)));
}
