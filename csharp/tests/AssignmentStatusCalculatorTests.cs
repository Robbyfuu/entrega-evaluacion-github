using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests del algebra PURA de estados de tarea extraida de
/// MainWindow.ComputeAssignmentStatusesAsync + UpdateAssignmentsBanner.
///
/// Congelan: las 5 senales (OWNED / ACCEPTED_DB / SUBMITTED / INVITED / EXPECTED),
/// el endurecimiento de EXPECTED por roster (aditivo, NUNCA bloquea OWNED/
/// ACCEPTED/SUBMITTED), la asociacion invitacion->tarea LONGEST-PREFIX-WINS
/// (delegada al core ClassroomRepoMatcher) y la proyeccion a 3 buckets DISJUNTOS.
/// </summary>
public class AssignmentStatusCalculatorTests
{
    // ===== Helpers =====

    private static AssignmentInput Asg(long id, string title, string? section = null, string? org = null)
        => new(id, title, section, org);

    private static AssignmentCalculation Compute(
        IReadOnlyList<AssignmentInput> assignments,
        IEnumerable<RepoInput>? repos = null,
        IEnumerable<long>? accepted = null,
        IEnumerable<SubmissionInput>? submissions = null,
        IReadOnlyList<InvitationInput>? invitations = null,
        string? me = "alumno",
        bool rosterMatchConfirmed = false,
        string? evaluationOrg = null)
        => AssignmentStatusCalculator.Compute(
            assignments,
            repos ?? Array.Empty<RepoInput>(),
            accepted ?? Array.Empty<long>(),
            submissions ?? Array.Empty<SubmissionInput>(),
            invitations,
            me,
            rosterMatchConfirmed,
            evaluationOrg);

    // ===== 5 senales individuales =====

    [Fact]
    public void OwnedOnly_RepoExists_MarksAcceptedWithRepoLink()
    {
        // OWNED: el repo esperado {slug}-{login} existe en la lista del alumno.
        var calc = Compute(
            new[] { Asg(1, "Tarea") },
            repos: new[] { new RepoInput("tarea-alumno", "alumno") });

        var s = Assert.Single(calc.Statuses);
        Assert.True(s.Accepted);
        Assert.Equal("tarea-alumno", s.RepoName);
        Assert.Equal("https://github.com/alumno/tarea-alumno", s.RepoUrl);
        Assert.False(s.Submitted);
        Assert.False(s.InvitationPending);

        var b = AssignmentStatusCalculator.ToBuckets(calc.Statuses);
        Assert.Equal(new AssignmentBuckets(0, 0, 1), b);
    }

    [Fact]
    public void OwnedRepo_UsesOwnerLoginWhenPresent_FallsBackToMe()
    {
        // El owner del repo manda la URL; si el owner viene null, cae al login.
        var calc = Compute(
            new[] { Asg(1, "Tarea") },
            repos: new[] { new RepoInput("tarea-alumno", "la-org") });
        Assert.Equal("https://github.com/la-org/tarea-alumno", calc.Statuses[0].RepoUrl);

        var calc2 = Compute(
            new[] { Asg(1, "Tarea") },
            repos: new[] { new RepoInput("tarea-alumno", null) });
        Assert.Equal("https://github.com/alumno/tarea-alumno", calc2.Statuses[0].RepoUrl);
    }

    [Fact]
    public void AcceptedDb_NoRepoNoSubmission_IsAcceptedPendingEntrega()
    {
        // ACCEPTED_DB: aceptacion registrada en BD, sin repo OWNED ni entrega.
        var calc = Compute(new[] { Asg(7, "Tarea") }, accepted: new long[] { 7 });

        var s = Assert.Single(calc.Statuses);
        Assert.True(s.Accepted);
        Assert.Null(s.RepoName);
        Assert.False(s.Submitted);
        Assert.Equal(new AssignmentBuckets(0, 0, 1), AssignmentStatusCalculator.ToBuckets(calc.Statuses));
    }

    [Fact]
    public void Submitted_IsTerminal_NoBucketCounts()
    {
        // SUBMITTED: entrega formal. Captura url/fecha y vacia los 3 buckets.
        var calc = Compute(
            new[] { Asg(3, "Tarea") },
            accepted: new long[] { 3 },
            submissions: new[] { new SubmissionInput(3, "https://github.com/alumno/tarea-alumno", "2026-06-01") });

        var s = Assert.Single(calc.Statuses);
        Assert.True(s.Submitted);
        Assert.Equal("https://github.com/alumno/tarea-alumno", s.SubmittedRepoUrl);
        Assert.Equal("2026-06-01", s.SubmittedAt);
        Assert.Equal(new AssignmentBuckets(0, 0, 0), AssignmentStatusCalculator.ToBuckets(calc.Statuses));
    }

    [Fact]
    public void Invited_NotAccepted_IsPendienteAceptar()
    {
        // INVITED: invitacion viva asociada por prefijo; aun no aceptada.
        var calc = Compute(
            new[] { Asg(1, "Tarea") },
            invitations: new[] { new InvitationInput(10, "tarea-login", null) });

        var s = Assert.Single(calc.Statuses);
        Assert.True(s.InvitationPending);
        Assert.Equal(10, s.InvitationId);
        Assert.False(s.Accepted);
        Assert.Empty(calc.Unassociated);
        Assert.Equal(new AssignmentBuckets(1, 0, 0), AssignmentStatusCalculator.ToBuckets(calc.Statuses));
    }

    [Fact]
    public void ExpectedOnly_NoSignals_IsEsperandoInvite()
    {
        // EXPECTED: la tarea existe pero no hay OWNED/ACCEPTED/SUBMITTED/INVITED.
        var calc = Compute(new[] { Asg(1, "Tarea") });

        var s = Assert.Single(calc.Statuses);
        Assert.False(s.Accepted);
        Assert.False(s.Submitted);
        Assert.False(s.InvitationPending);
        Assert.Equal(new AssignmentBuckets(0, 1, 0), AssignmentStatusCalculator.ToBuckets(calc.Statuses));
    }

    // ===== Roster: ADITIVO, solo endurece EXPECTED, NUNCA bloquea =====

    [Fact]
    public void RosterConfirmed_SuppressesExpectedOnly_GlobalSection()
    {
        // Con match confirmado, una fila EXPECTED-only de seccion GLOBAL/vacia
        // se omite (la pista global ya no hace falta).
        var calc = Compute(
            new[] { Asg(1, "Tarea", section: null) },
            rosterMatchConfirmed: true);

        Assert.Empty(calc.Statuses);
    }

    [Fact]
    public void RosterConfirmed_NeverSuppressesOwnedAcceptedOrSubmitted()
    {
        // CRITICO: roster NUNCA bloquea una tarea poseida/aceptada/entregada,
        // aunque sea de seccion global. Una entrega pendiente real no se suprime.
        var owned = Compute(
            new[] { Asg(1, "Tarea", section: null) },
            repos: new[] { new RepoInput("tarea-alumno", "alumno") },
            rosterMatchConfirmed: true);
        Assert.Single(owned.Statuses);

        var accepted = Compute(
            new[] { Asg(2, "Tarea", section: null) },
            accepted: new long[] { 2 },
            rosterMatchConfirmed: true);
        Assert.Single(accepted.Statuses);

        var submitted = Compute(
            new[] { Asg(3, "Tarea", section: null) },
            accepted: new long[] { 3 },
            submissions: new[] { new SubmissionInput(3, "u", "t") },
            rosterMatchConfirmed: true);
        Assert.Single(submitted.Statuses);
    }

    [Fact]
    public void RosterConfirmed_DoesNotSuppressExpectedWithSection()
    {
        // EXPECTED-only pero con seccion explicita: NO se omite.
        var calc = Compute(
            new[] { Asg(1, "Tarea", section: "001D") },
            rosterMatchConfirmed: true);
        Assert.Single(calc.Statuses);
    }

    [Fact]
    public void NoRosterMatch_ExpectedOnlyGlobal_NotSuppressed()
    {
        // Sin match confirmado (default): nada se omite, ni EXPECTED-only global.
        var calc = Compute(
            new[] { Asg(1, "Tarea", section: null) },
            rosterMatchConfirmed: false);
        Assert.Single(calc.Statuses);
    }

    [Fact]
    public void RosterSuppression_OnInvitedGlobalTask_DropsInvitationToUnassociated()
    {
        // Interaccion sutil PRESERVADA: la suppresion por roster corre en el loop
        // por-tarea ANTES de asociar invitaciones y NO mira InvitationPending. Una
        // tarea EXPECTED-only de seccion global, con match confirmado, se suprime
        // aunque exista una invitacion para ella -> esa invitacion queda sin asociar.
        var calc = Compute(
            new[] { Asg(1, "Tarea", section: null) },
            invitations: new[] { new InvitationInput(42, "tarea-login", null) },
            rosterMatchConfirmed: true);

        Assert.Empty(calc.Statuses);
        var u = Assert.Single(calc.Unassociated);
        Assert.Equal(42, u.Id);
    }

    // ===== LONGEST-PREFIX-WINS (delegado a ClassroomRepoMatcher) =====

    [Fact]
    public void Invitation_LongestPrefixWins_OverShorterPrefix()
    {
        // "tarea-extra-login" matchea ambos prefijos; gana "Tarea Extra".
        var calc = Compute(
            new[] { Asg(1, "Tarea"), Asg(2, "Tarea Extra") },
            invitations: new[] { new InvitationInput(10, "tarea-extra-login", null) });

        var corta = calc.Statuses.Single(s => s.AssignmentId == 1);
        var larga = calc.Statuses.Single(s => s.AssignmentId == 2);
        Assert.False(corta.InvitationPending);   // no robada por el slug corto
        Assert.True(larga.InvitationPending);
        Assert.Equal(10, larga.InvitationId);
        Assert.Empty(calc.Unassociated);
    }

    [Fact]
    public void Invitation_ShortRepo_GoesToShortAssignment()
    {
        // "tarea-login" solo matchea "tarea-" -> la tarea corta, no la extra.
        var calc = Compute(
            new[] { Asg(1, "Tarea"), Asg(2, "Tarea Extra") },
            invitations: new[] { new InvitationInput(11, "tarea-login", null) });

        Assert.True(calc.Statuses.Single(s => s.AssignmentId == 1).InvitationPending);
        Assert.False(calc.Statuses.Single(s => s.AssignmentId == 2).InvitationPending);
    }

    [Fact]
    public void Invitation_EachClaimedByAtMostOneAssignment()
    {
        // Matching bipartito: dos invitaciones, cada una a su tarea por prefijo.
        var calc = Compute(
            new[] { Asg(1, "Tarea"), Asg(2, "Tarea Extra") },
            invitations: new[]
            {
                new InvitationInput(10, "tarea-extra-login", null),
                new InvitationInput(11, "tarea-login", null),
            });

        Assert.Equal(11, calc.Statuses.Single(s => s.AssignmentId == 1).InvitationId);
        Assert.Equal(10, calc.Statuses.Single(s => s.AssignmentId == 2).InvitationId);
        Assert.Empty(calc.Unassociated);
    }

    // ===== Invitaciones sin asociar =====

    [Fact]
    public void Invitation_NoPrefixMatch_GoesToUnassociated()
    {
        var calc = Compute(
            new[] { Asg(1, "Tarea") },
            invitations: new[] { new InvitationInput(99, "otra-cosa-login", null) });

        Assert.False(calc.Statuses.Single().InvitationPending);
        var u = Assert.Single(calc.Unassociated);
        Assert.Equal(99, u.Id);
    }

    [Fact]
    public void NoAssignments_AllInvitationsUnassociated()
    {
        var calc = Compute(
            Array.Empty<AssignmentInput>(),
            invitations: new[] { new InvitationInput(5, "x-login", null) });

        Assert.Empty(calc.Statuses);
        Assert.Single(calc.Unassociated);
    }

    [Fact]
    public void NoAssignments_NullInvitations_EmptyEverything()
    {
        var calc = Compute(Array.Empty<AssignmentInput>(), invitations: null);
        Assert.Empty(calc.Statuses);
        Assert.Empty(calc.Unassociated);
    }

    [Fact]
    public void NullInvitations_NeverMarksPending_NorUnassociated()
    {
        // null = no se pudo consultar (distinto de lista vacia): nada pendiente
        // por invitacion y nada sin asociar.
        var calc = Compute(new[] { Asg(1, "Tarea") }, invitations: null);
        Assert.False(calc.Statuses.Single().InvitationPending);
        Assert.Empty(calc.Unassociated);
    }

    // ===== 3 buckets DISJUNTOS =====

    [Theory]
    // invitationPending, accepted, submitted -> (aceptar, esperando, entregar)
    [InlineData(false, false, false, 0, 1, 0)] // EXPECTED puro
    [InlineData(true, false, false, 1, 0, 0)]  // INVITED no aceptada
    [InlineData(false, true, false, 0, 0, 1)]  // ACCEPTED/OWNED no entregada
    [InlineData(true, true, false, 0, 0, 1)]   // invitada + aceptada -> entregar
    [InlineData(false, true, true, 0, 0, 0)]   // entregada
    [InlineData(true, true, true, 0, 0, 0)]    // entregada (terminal)
    [InlineData(false, false, true, 0, 0, 0)]  // submitted sin accept (defensivo)
    [InlineData(true, false, true, 1, 0, 0)]   // invited + submitted sin accept
    public void ToBuckets_MatchesOriginalPredicates(
        bool inv, bool acc, bool sub, int aceptar, int esperando, int entregar)
    {
        var b = AssignmentStatusCalculator.ToBuckets(new[] { (inv, acc, sub) });
        Assert.Equal(new AssignmentBuckets(aceptar, esperando, entregar), b);
    }

    [Fact]
    public void Buckets_AreDisjoint_EveryStatusInAtMostOne()
    {
        // Pin de DISJUNTEZ: para CUALQUIER combinacion de senales, un status
        // suma a lo sumo a UN bucket.
        foreach (var inv in new[] { false, true })
        foreach (var acc in new[] { false, true })
        foreach (var sub in new[] { false, true })
        {
            var b = AssignmentStatusCalculator.ToBuckets(new[] { (inv, acc, sub) });
            var fired =
                (b.PendienteAceptar > 0 ? 1 : 0) +
                (b.EsperandoInvite > 0 ? 1 : 0) +
                (b.PendienteEntregar > 0 ? 1 : 0);
            Assert.True(fired <= 1, $"inv={inv} acc={acc} sub={sub} encendio {fired} buckets");
        }
    }

    [Fact]
    public void PendingActionable_IsSumOfThreeBuckets()
    {
        var b = new AssignmentBuckets(2, 3, 4);
        Assert.Equal(9, b.PendingActionable);
    }

    [Fact]
    public void MixedScenario_BucketsSumAndDisjointness()
    {
        // Escenario integrado: una de cada bucket + una entregada + una suprimida
        // por roster. Verifica conteos exactos y total accionable.
        // Las que deben sobrevivir al roster llevan seccion explicita; solo
        // Epsilon es EXPECTED-only global -> suprimida. (La suppresion corre
        // ANTES de asociar invitaciones; ver RosterSuppression_OnInvitedGlobalTask
        // _DropsInvitationToUnassociated.)
        var calc = Compute(
            new[]
            {
                Asg(1, "Alfa", section: "001D"),  // EXPECTED puro -> esperando
                Asg(2, "Beta", section: "001D"),  // INVITED -> aceptar
                Asg(3, "Gamma", section: "001D"), // ACCEPTED -> entregar
                Asg(4, "Delta", section: "001D"), // SUBMITTED -> nada
                Asg(5, "Epsilon", section: null)  // EXPECTED-only global -> suprimida por roster
            },
            accepted: new long[] { 3, 4 },
            submissions: new[] { new SubmissionInput(4, "u", "t") },
            invitations: new[] { new InvitationInput(20, "beta-login", null) },
            rosterMatchConfirmed: true);

        // Epsilon suprimida por roster (EXPECTED-only + seccion global).
        Assert.DoesNotContain(calc.Statuses, s => s.AssignmentId == 5);
        Assert.Equal(4, calc.Statuses.Count);

        var b = AssignmentStatusCalculator.ToBuckets(calc.Statuses);
        Assert.Equal(new AssignmentBuckets(1, 1, 1), b);
        Assert.Equal(3, b.PendingActionable);
    }
}
