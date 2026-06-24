// Modelo y helpers del workspace centrado en seccion (Secciones -> alumnos ->
// detalle). Unifica roster (v_enrollment_status) con el estado vivo
// (online_clients): aceptacion/entrega + presencia + procesos + version +
// lockdown en una sola fila por alumno.

import type {
  EnrollmentStatusRow,
  OnlineClientRow,
  SuspiciousProcess,
} from "@/lib/types";
import { FALLBACK_SUSPICIOUS_PROCESSES, normalizeProcessName } from "@/lib/suspicious";

export const ONLINE_WINDOW_MS = 90_000;

// Fila unificada por alumno para la tabla de la seccion.
export interface UnifiedStudent {
  key: string;
  fullName: string | null;
  github: string | null;
  enrolled: boolean; // true = esta en el roster; false = vivo pero fuera de roster (huerfano)
  accepted: boolean;
  submitted: boolean;
  githubResolved: boolean;
  online: boolean;
  client: OnlineClientRow | null;
  suspCount: number;
  version: string | null;
  lockdown: boolean;
}

// Predicado de proceso sospechoso, con la misma logica que OnlineClientsSection:
// global (section null) o scoped a la seccion del alumno; fallback al set
// estatico si la tabla esta vacia.
export function makeSuspChecker(
  suspiciousRows: SuspiciousProcess[]
): (procName: string | null | undefined, sectionCode: string | null) => boolean {
  const globalSet = new Set<string>();
  const bySection = new Map<string, Set<string>>();
  for (const r of suspiciousRows) {
    const norm = normalizeProcessName(r.process_name);
    if (!norm) continue;
    if (r.section === null) {
      globalSet.add(norm);
    } else {
      const set = bySection.get(r.section) ?? new Set<string>();
      set.add(norm);
      bySection.set(r.section, set);
    }
  }
  const useFallback = suspiciousRows.length === 0;
  return (procName, sectionCode) => {
    const norm = normalizeProcessName(procName ?? "");
    if (!norm) return false;
    if (useFallback) return FALLBACK_SUSPICIOUS_PROCESSES.has(norm);
    if (globalSet.has(norm)) return true;
    if (sectionCode) return bySection.get(sectionCode)?.has(norm) ?? false;
    return false;
  };
}

// Cuenta procesos sospechosos de un cliente vivo.
function suspCountFor(
  client: OnlineClientRow | null,
  isSuspiciousFor: (n: string | null | undefined, s: string | null) => boolean,
  sectionCode: string | null
): number {
  const procs = client?.processes;
  if (!Array.isArray(procs)) return 0;
  return procs.filter((p) => isSuspiciousFor(p.name, sectionCode)).length;
}

// Construye la lista unificada: roster enriquecido con el cliente vivo, mas los
// alumnos vivos que NO estan en el roster (huerfanos), al final.
export function buildStudents(args: {
  rosterStatus: EnrollmentStatusRow[]; // ya filtrado a source="roster" + seccion
  onlineClients: OnlineClientRow[]; // ya filtrado a la seccion + online
  isSuspiciousFor: (n: string | null | undefined, s: string | null) => boolean;
  sectionCode: string | null;
  // Aceptó/entregó SCOPEADO a la(s) tarea(s) activa(s) de la seccion/evaluacion
  // (github en minuscula). NO usamos el accepted/submitted de la vista porque
  // ese cuenta CUALQUIER entrega historica del alumno -> marcaba entregado para
  // una evaluacion nueva por una entrega de una tarea vieja ya desactivada.
  acceptedSet: Set<string>;
  submittedSet: Set<string>;
}): UnifiedStudent[] {
  const { rosterStatus, onlineClients, isSuspiciousFor, sectionCode, acceptedSet, submittedSet } = args;

  const byGithub = new Map<string, OnlineClientRow>();
  for (const c of onlineClients) {
    if (c.github_username) byGithub.set(c.github_username.toLowerCase(), c);
  }

  const matched = new Set<string>();
  const rosterRows: UnifiedStudent[] = rosterStatus.map((r) => {
    const gh = r.github_username?.toLowerCase() ?? null;
    const client = gh ? byGithub.get(gh) ?? null : null;
    if (gh && client) matched.add(gh);
    return {
      key: `roster-${r.enrollment_id ?? r.github_username ?? r.full_name ?? Math.random()}`,
      fullName: r.full_name,
      github: r.github_username,
      enrolled: true,
      accepted: gh ? acceptedSet.has(gh) : false,
      submitted: gh ? submittedSet.has(gh) : false,
      githubResolved: r.github_resolved,
      online: !!client,
      client,
      suspCount: suspCountFor(client, isSuspiciousFor, sectionCode),
      version: client?.app_version ?? null,
      lockdown: client?.lockdown_state === "active",
    };
  });

  // Huerfanos: vivos en la seccion con github que no matchea ningun roster row.
  const orphans: UnifiedStudent[] = [];
  for (const c of onlineClients) {
    const gh = c.github_username?.toLowerCase() ?? null;
    if (gh && matched.has(gh)) continue;
    orphans.push({
      key: `orphan-${c.pc_name ?? ""}-${c.github_username ?? ""}`,
      fullName: null,
      github: c.github_username,
      enrolled: false,
      accepted: gh ? acceptedSet.has(gh) : false,
      submitted: gh ? submittedSet.has(gh) : false,
      githubResolved: !!c.github_username,
      online: true,
      client: c,
      suspCount: suspCountFor(c, isSuspiciousFor, sectionCode),
      version: c.app_version ?? null,
      lockdown: c.lockdown_state === "active",
    });
  }

  // Orden: online primero, luego por nombre/github.
  rosterRows.sort((a, b) => {
    if (a.online !== b.online) return a.online ? -1 : 1;
    return (a.fullName ?? a.github ?? "").localeCompare(b.fullName ?? b.github ?? "");
  });

  return [...rosterRows, ...orphans];
}

// Aceptó/entregó scopeado a las tareas ACTIVAS de una seccion. Cruza por
// assignment_id (no por "cualquier entrega del alumno"): una entrega de una
// tarea vieja ya DESACTIVADA no cuenta para la evaluacion actual. Si evalId !=
// null, restringe ademas a las tareas de esa evaluacion.
export interface ScopedStatus {
  acceptedSet: Set<string>; // github en minuscula
  submittedSet: Set<string>;
}

export function scopedStatusForSection(args: {
  sectionCode: string | null;
  evalId: number | null;
  assignments: { id: number | string; section: string | null; evaluation_id: number | null; active: boolean }[];
  acceptances: { github_username: string | null; assignment_id: number | string }[];
  submissions: { github_username: string | null; assignment_id: number | string | null }[];
}): ScopedStatus {
  const { sectionCode, evalId, assignments, acceptances, submissions } = args;
  const code = (sectionCode ?? "").toUpperCase();

  // Ids de tareas ACTIVAS de la seccion (o globales), opcionalmente de una eval.
  const aids = new Set<number>();
  for (const a of assignments) {
    if (!a.active) continue;
    const aCode = (a.section ?? "").toUpperCase();
    if (aCode !== "" && aCode !== code) continue;
    if (evalId != null && Number(a.evaluation_id) !== evalId) continue;
    aids.add(Number(a.id));
  }

  const acceptedSet = new Set<string>();
  for (const x of acceptances) {
    if (x.github_username && aids.has(Number(x.assignment_id)))
      acceptedSet.add(x.github_username.toLowerCase());
  }
  const submittedSet = new Set<string>();
  for (const x of submissions) {
    if (x.github_username && x.assignment_id != null && aids.has(Number(x.assignment_id)))
      submittedSet.add(x.github_username.toLowerCase());
  }
  return { acceptedSet, submittedSet };
}

// Estadisticas por seccion para las tarjetas del nivel 1.
export interface SectionStats {
  online: number;
  enrolled: number | null;
  accepted: number;
  submitted: number;
  suspicious: number; // alumnos online con >=1 proceso sospechoso
}
