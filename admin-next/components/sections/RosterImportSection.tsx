"use client";

import { useCallback, useMemo, useRef, useState } from "react";
import type { EnrollmentRow, EnrollmentStatusRow } from "@/lib/types";
import { useEnrollments, type ImportStudent, type ImportSummary } from "@/hooks/useEnrollments";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";

// Shape of the bb-dl roster-{section}.json document (see bb-dl src/roster.ts).
interface RosterFile {
  section: string;
  courseId: string;
  students: Array<{
    blackboard_student_id: string;
    full_name: string;
    email: string | null;
    github_username: string | null;
  }>;
}

interface Feedback {
  text: string;
  ok: boolean;
}

// Validates the parsed JSON has the roster shape. Throws on a malformed file so
// the import hard-fails before touching the DB (never a partial/silent import).
function parseRoster(raw: unknown): RosterFile {
  if (typeof raw !== "object" || raw === null) {
    throw new Error("El archivo no es un roster válido (no es un objeto JSON).");
  }
  const obj = raw as Record<string, unknown>;
  if (typeof obj.section !== "string" || !obj.section) {
    throw new Error('El roster no tiene un campo "section" válido.');
  }
  if (!Array.isArray(obj.students)) {
    throw new Error('El roster no tiene una lista "students".');
  }
  const students = obj.students.map((s, i) => {
    if (typeof s !== "object" || s === null) {
      throw new Error(`El alumno #${i + 1} no es un objeto válido.`);
    }
    const so = s as Record<string, unknown>;
    if (typeof so.blackboard_student_id !== "string" || !so.blackboard_student_id) {
      throw new Error(`El alumno #${i + 1} no tiene blackboard_student_id.`);
    }
    if (typeof so.full_name !== "string" || !so.full_name) {
      throw new Error(`El alumno #${i + 1} no tiene full_name.`);
    }
    return {
      blackboard_student_id: so.blackboard_student_id,
      full_name: so.full_name,
      email: typeof so.email === "string" ? so.email : null,
      github_username:
        typeof so.github_username === "string" && so.github_username
          ? so.github_username
          : null,
    };
  });
  return {
    section: obj.section,
    courseId: typeof obj.courseId === "string" ? obj.courseId : "",
    students,
  };
}

export function RosterImportSection() {
  const { enrollments, status, loading, error, importRoster, setGithub } = useEnrollments();
  const { sections, sectionById, courseById } = useSectionLookup();

  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [summary, setSummary] = useState<ImportSummary | null>(null);
  const [importing, setImporting] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Inline github edit state: the enrollment id being edited and its draft.
  const [editingId, setEditingId] = useState<number | null>(null);
  const [draftGithub, setDraftGithub] = useState("");

  // Section view filter.
  const [viewSectionId, setViewSectionId] = useState<string>("");

  const sectionLabel = useCallback(
    (sectionId: number | null) => {
      if (sectionId == null) return "—";
      const sec = sectionById.get(sectionId);
      if (!sec) return `#${sectionId}`;
      const course = courseById.get(sec.course_id);
      return `${course?.code ?? "?"} / ${sec.code}`;
    },
    [sectionById, courseById]
  );

  // Resolve a roster section code to a sections.id. Hard-fails on unknown or
  // ambiguous code: never silently creates a section nor drops students.
  const resolveSectionId = useCallback(
    (code: string): number => {
      const matches = sections.filter((s) => s.code === code);
      if (matches.length === 0) {
        throw new Error(
          `Sección desconocida "${code}". Créala en "Secciones" antes de importar el roster (no se importó ningún alumno).`
        );
      }
      if (matches.length > 1) {
        throw new Error(
          `El código de sección "${code}" existe en más de un curso. Desambigua antes de importar (no se importó ningún alumno).`
        );
      }
      return matches[0]!.id;
    },
    [sections]
  );

  const onFilePicked = useCallback(
    async (file: File) => {
      setFeedback(null);
      setSummary(null);
      setImporting(true);
      try {
        const text = await file.text();
        let raw: unknown;
        try {
          raw = JSON.parse(text);
        } catch {
          throw new Error("El archivo no es JSON válido.");
        }
        const roster = parseRoster(raw);
        // Hard-fail BEFORE any write if the section code is unknown.
        const sectionId = resolveSectionId(roster.section);
        if (roster.students.length === 0) {
          throw new Error("El roster no contiene alumnos.");
        }
        const students: ImportStudent[] = roster.students;
        const result = await importRoster(sectionId, students);
        setSummary(result);
        setFeedback({
          text: `Roster "${roster.section}" importado: ${result.inserted} nuevos, ${result.updated} actualizados.`,
          ok: true,
        });
        setViewSectionId(String(sectionId));
      } catch (e) {
        setFeedback({
          text: e instanceof Error ? e.message : "Error al importar el roster.",
          ok: false,
        });
      } finally {
        setImporting(false);
        if (fileInputRef.current) fileInputRef.current.value = "";
      }
    },
    [importRoster, resolveSectionId]
  );

  // Enrollments filtered to the section shown in the roster table.
  const visibleEnrollments = useMemo(() => {
    if (!viewSectionId) return enrollments;
    const sid = Number(viewSectionId);
    return enrollments.filter((e) => e.section_id === sid);
  }, [enrollments, viewSectionId]);

  async function commitGithub(enrollment: EnrollmentRow) {
    const value = draftGithub.trim();
    try {
      await setGithub(enrollment.id, value || null);
      setEditingId(null);
      setDraftGithub("");
      setFeedback({ text: `Github actualizado para ${enrollment.full_name}.`, ok: true });
    } catch (e) {
      setFeedback({
        text: e instanceof Error ? e.message : "No se pudo asignar el github.",
        ok: false,
      });
    }
  }

  // --- Validation / conflict buckets from v_enrollment_status (reads only) ---

  const rosterRows = useMemo(
    () => status.filter((r) => r.source === "roster"),
    [status]
  );

  // (a) enrolled but no github assigned yet.
  const missingGithub = useMemo(
    () => rosterRows.filter((r) => !r.github_resolved),
    [rosterRows]
  );

  // (b) non-submitters: enrolled, has github, but never submitted. The
  // denominator is the roster (rows with a resolved github), not submissions.
  const githubResolvedRoster = useMemo(
    () => rosterRows.filter((r) => r.github_resolved),
    [rosterRows]
  );
  const nonSubmitters = useMemo(
    () => githubResolvedRoster.filter((r) => !r.submitted),
    [githubResolvedRoster]
  );

  // (c) orphan activity: github with activity but no enrollment in its section.
  const orphans = useMemo(
    () => status.filter((r) => r.source === "orphan"),
    [status]
  );

  // (d) "sección sin resolver": activity whose section could not be resolved.
  // Kept separate from orphans.
  const unresolvedSection = useMemo(
    () => status.filter((r) => r.source === "unresolved_section"),
    [status]
  );

  return (
    <div className="card" id="sec-roster">
      <h2>
        Roster (Blackboard)
        <span className="pill">{enrollments.length}</span>
      </h2>
      <p className="muted-note">
        Importa el archivo <span className="mono">roster-&#123;sección&#125;.json</span> que genera{" "}
        <span className="mono">bb-dl --roster</span>. La sección del archivo debe existir en{" "}
        <strong>Secciones</strong> (si no, el import se aborta sin escribir nada). El github se
        asigna a mano más abajo.
      </p>

      <div className="row-flex">
        <div className="field">
          <label htmlFor="rosterFile">Archivo roster JSON</label>
          <input
            ref={fileInputRef}
            type="file"
            id="rosterFile"
            accept="application/json,.json"
            disabled={importing}
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) void onFilePicked(f);
            }}
          />
        </div>
        {importing ? (
          <span style={{ alignSelf: "flex-end", color: "var(--text-faint)" }}>Importando…</span>
        ) : null}
      </div>

      {feedback ? <div className={feedback.ok ? "ok" : "err"}>{feedback.text}</div> : null}

      {summary ? (
        <div className="ok" style={{ marginTop: 8 }}>
          Resumen: {summary.inserted} insertados · {summary.updated} actualizados ·{" "}
          {summary.githubResolved} con github · {summary.githubNull} sin github (total{" "}
          {summary.total}).
        </div>
      ) : null}

      {error ? (
        <p className="err" style={{ marginTop: 16 }}>
          Error al leer enrollments: {error}
        </p>
      ) : null}

      {/* -------- Roster por sección + asignación manual de github -------- */}
      <div className="row-flex" style={{ marginTop: 16 }}>
        <div className="field" style={{ flex: "0 0 220px" }}>
          <label htmlFor="rosterViewSection">Ver sección</label>
          <select
            id="rosterViewSection"
            value={viewSectionId}
            onChange={(e) => setViewSectionId(e.target.value)}
          >
            <option value="">Todas las secciones</option>
            {sections.map((s) => (
              <option key={s.id} value={s.id}>
                {courseById.get(s.course_id)?.code ?? "?"} / {s.code}
              </option>
            ))}
          </select>
        </div>
      </div>

      {loading && enrollments.length === 0 ? (
        <p style={{ marginTop: 16, color: "var(--text-faint)" }}>Cargando…</p>
      ) : (
        <table style={{ marginTop: 8 }}>
          <thead>
            <tr>
              <th style={{ width: "12%" }}>Sección</th>
              <th style={{ width: "26%" }}>Nombre</th>
              <th style={{ width: "22%" }}>Email</th>
              <th style={{ width: "12%" }}>Estado</th>
              <th style={{ width: "28%" }}>Github</th>
            </tr>
          </thead>
          <tbody>
            {visibleEnrollments.length === 0 ? (
              <tr>
                <td colSpan={5} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                  Sin alumnos en el roster. Importa un archivo roster JSON.
                </td>
              </tr>
            ) : (
              visibleEnrollments.map((e) => (
                <tr key={e.id}>
                  <td>
                    <Badge solidColor={BADGE.sectionAlt}>{sectionLabel(e.section_id)}</Badge>
                  </td>
                  <td>{e.full_name}</td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {e.email ?? <span style={{ color: "var(--text-faint)" }}>—</span>}
                  </td>
                  <td>
                    <Badge solidColor={BADGE.success}>{e.status}</Badge>
                  </td>
                  <td>
                    {editingId === e.id ? (
                      <div style={{ display: "flex", gap: 6, alignItems: "center" }}>
                        <input
                          type="text"
                          value={draftGithub}
                          placeholder="usuario-github"
                          autoFocus
                          onChange={(ev) => setDraftGithub(ev.target.value)}
                          onKeyDown={(ev) => {
                            if (ev.key === "Enter") void commitGithub(e);
                            if (ev.key === "Escape") {
                              setEditingId(null);
                              setDraftGithub("");
                            }
                          }}
                          style={{ flex: 1 }}
                        />
                        <button
                          className="btn-success"
                          style={{ padding: "4px 10px", fontSize: 12, height: "auto" }}
                          onClick={() => void commitGithub(e)}
                        >
                          ✓
                        </button>
                        <button
                          className="btn-secondary"
                          style={{ padding: "4px 10px", fontSize: 12, height: "auto" }}
                          onClick={() => {
                            setEditingId(null);
                            setDraftGithub("");
                          }}
                        >
                          ×
                        </button>
                      </div>
                    ) : (
                      <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
                        {e.github_username ? (
                          <span className="mono">{e.github_username}</span>
                        ) : (
                          <Badge solidColor={BADGE.neutral}>sin github</Badge>
                        )}
                        <button
                          className="btn-secondary"
                          style={{ padding: "4px 10px", fontSize: 12, height: "auto" }}
                          onClick={() => {
                            setEditingId(e.id);
                            setDraftGithub(e.github_username ?? "");
                          }}
                        >
                          {e.github_username ? "Editar" : "Asignar"}
                        </button>
                      </div>
                    )}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      )}

      {/* -------- Validación / conflictos (vista v_enrollment_status) -------- */}
      <h3 style={{ marginTop: 24 }}>Validación cruzada</h3>
      <div className="roster-validation-grid">
        <ValidationCard
          title="Falta asignar github"
          count={missingGithub.length}
          color={BADGE.neutral}
          empty="Todas las inscripciones tienen github."
          rows={missingGithub.map((r) => ({
            key: `mg-${r.enrollment_id}`,
            label: r.full_name ?? "—",
            sub: sectionLabel(r.section_id),
          }))}
        />
        <ValidationCard
          title={`No entregaron (${nonSubmitters.length} de ${githubResolvedRoster.length})`}
          count={nonSubmitters.length}
          color={BADGE.danger}
          empty="Todos los alumnos con github entregaron."
          rows={nonSubmitters.map((r) => ({
            key: `ns-${r.enrollment_id}`,
            label: r.full_name ?? r.github_username ?? "—",
            sub: `${sectionLabel(r.section_id)}${r.accepted ? " · aceptó" : ""}`,
          }))}
        />
        <ValidationCard
          title="Github huérfano"
          count={orphans.length}
          color={BADGE.danger}
          empty="Sin actividad huérfana."
          rows={orphans.map((r, i) => ({
            key: `or-${r.github_username}-${r.section_id}-${i}`,
            label: r.github_username ?? "—",
            sub: `${sectionLabel(r.section_id)} · sin inscripción`,
          }))}
        />
        <ValidationCard
          title="Sección sin resolver"
          count={unresolvedSection.length}
          color={BADGE.neutral}
          empty="Toda la actividad resolvió su sección."
          rows={unresolvedSection.map((r, i) => ({
            key: `us-${r.github_username}-${i}`,
            label: r.github_username ?? "—",
            sub: "sección no resuelta",
          }))}
        />
      </div>
    </div>
  );
}

interface ValidationRow {
  key: string;
  label: string;
  sub: string;
}

interface ValidationCardProps {
  title: string;
  count: number;
  color: string;
  empty: string;
  rows: ValidationRow[];
}

function ValidationCard({ title, count, color, empty, rows }: ValidationCardProps) {
  return (
    <div className="roster-validation-card">
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8 }}>
        <strong>{title}</strong>
        <Badge solidColor={color}>{count}</Badge>
      </div>
      {rows.length === 0 ? (
        <p style={{ color: "var(--text-faint)", fontSize: 12, margin: 0 }}>{empty}</p>
      ) : (
        <ul style={{ margin: 0, paddingLeft: 16, fontSize: 13 }}>
          {rows.map((r) => (
            <li key={r.key}>
              {r.label}{" "}
              <span style={{ color: "var(--text-faint)", fontSize: 11 }}>· {r.sub}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
