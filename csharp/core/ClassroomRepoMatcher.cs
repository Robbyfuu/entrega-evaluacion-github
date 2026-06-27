namespace EntregaEvaluacion.Core;

/// <summary>
/// Core compartido de asociacion invitacion -> tarea con LONGEST-PREFIX-WINS.
///
/// Generico sobre el tipo de candidato: el caller provee como obtener el titulo
/// (de donde sale el prefijo de slug) y la org esperada de cada candidato, asi
/// el algoritmo no depende de ningun DTO concreto.
///
/// Entre los candidatos cuyo prefijo de slug ({Sanitize(title)}-) es prefijo del
/// nombre del repo invitado, gana el del prefijo MAS LARGO (mas especifico): asi
/// "Tarea Extra" ("tarea-extra-") reclama "tarea-extra-login" en vez de "Tarea"
/// ("tarea-"), eliminando la colision de prefijos dependiente del orden.
/// Desempate entre prefijos de IGUAL longitud: preferir el candidato cuyo
/// expectedOrg (evalOrg ?? orgOf(candidate)) coincide con el inviter; ante empate
/// total gana el primero en orden estable de entrada. Devuelve null si ninguno
/// matchea. El resultado es DETERMINISTA.
/// </summary>
public static class ClassroomRepoMatcher
{
    public static T? PickByLongestPrefix<T>(
        IEnumerable<T> candidates, string repoName, string? inviter, string? evalOrg,
        Func<T, string> titleOf, Func<T, string?> orgOf) where T : class
    {
        if (string.IsNullOrEmpty(repoName)) return null;

        T? best = null;
        int bestLen = -1;
        bool bestOrgMatch = false;
        foreach (var a in candidates)
        {
            var prefix = ClassroomRepoNaming.ClassroomRepoPrefix(titleOf(a));
            if (!repoName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var expectedOrg = evalOrg ?? orgOf(a);
            bool orgMatch = !string.IsNullOrEmpty(expectedOrg)
                && string.Equals(inviter, expectedOrg, StringComparison.OrdinalIgnoreCase);

            // Prioridad: (1) prefijo mas largo; (2) a igual longitud, el que
            // coincide en org. Determinista: el primer candidato estable gana
            // ante empate total.
            if (prefix.Length > bestLen
                || (prefix.Length == bestLen && orgMatch && !bestOrgMatch))
            {
                best = a;
                bestLen = prefix.Length;
                bestOrgMatch = orgMatch;
            }
        }
        return best;
    }
}
