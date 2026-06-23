"use client";

import { useCallback, useMemo, useRef, useState } from "react";
import { Check, CheckCircle2, Users, X, XCircle } from "lucide-react";
import type { EnrollmentRow } from "@/lib/types";
import { useEnrollments, type ImportStudent, type ImportSummary } from "@/hooks/useEnrollments";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

// Sentinel for the radix Select item mapped to the empty filter ("" = todas).
const ALL_VALUE = "__all__";

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
    <Card id="sec-roster" className="mb-4 scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          Roster (Blackboard)
          <Badge variant="neutral">{enrollments.length}</Badge>
        </CardTitle>
        <CardDescription>
          Importa el archivo{" "}
          <span className="font-mono">roster-&#123;sección&#125;.json</span> que
          genera <span className="font-mono">bb-dl --roster</span>. La sección del
          archivo debe existir en{" "}
          <strong className="font-semibold text-foreground">Secciones</strong> (si
          no, el import se aborta sin escribir nada). El github se asigna a mano
          más abajo.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        {/* Subida de archivo */}
        <div className="flex flex-col gap-1.5 sm:max-w-md">
          <Label htmlFor="rosterFile">Archivo roster JSON</Label>
          <div className="flex items-center gap-3">
            <Input
              ref={fileInputRef}
              type="file"
              id="rosterFile"
              accept="application/json,.json"
              disabled={importing}
              className="cursor-pointer file:mr-3 file:cursor-pointer file:rounded file:bg-secondary file:px-3 file:py-1 file:text-secondary-foreground"
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) void onFilePicked(f);
              }}
            />
            {importing ? (
              <span className="shrink-0 text-sm text-muted-foreground">
                Importando…
              </span>
            ) : null}
          </div>
        </div>

        {feedback ? (
          <div
            className={cn(
              "flex items-start gap-2 rounded-md border px-3 py-2 text-sm",
              feedback.ok
                ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400"
                : "border-destructive/30 bg-destructive/10 text-destructive"
            )}
          >
            {feedback.ok ? (
              <CheckCircle2 className="mt-0.5 size-4 shrink-0" />
            ) : (
              <XCircle className="mt-0.5 size-4 shrink-0" />
            )}
            <span>{feedback.text}</span>
          </div>
        ) : null}

        {summary ? (
          <div className="rounded-md border border-emerald-500/30 bg-emerald-500/10 px-3 py-2 text-sm text-emerald-600 dark:text-emerald-400">
            Resumen: {summary.inserted} insertados · {summary.updated} actualizados ·{" "}
            {summary.githubResolved} con github · {summary.githubNull} sin github
            (total {summary.total}).
          </div>
        ) : null}

        {error ? (
          <p className="text-sm text-destructive">
            Error al leer enrollments: {error}
          </p>
        ) : null}

        {/* -------- Roster por sección + asignación manual de github -------- */}
        <div className="flex w-full flex-col gap-1.5 sm:w-56">
          <Label htmlFor="rosterViewSection">Ver sección</Label>
          <Select
            value={viewSectionId === "" ? ALL_VALUE : viewSectionId}
            onValueChange={(value) =>
              setViewSectionId(value === ALL_VALUE ? "" : value)
            }
          >
            <SelectTrigger id="rosterViewSection" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_VALUE}>Todas las secciones</SelectItem>
              {sections.map((s) => (
                <SelectItem key={s.id} value={String(s.id)}>
                  {courseById.get(s.course_id)?.code ?? "?"} / {s.code}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {loading && enrollments.length === 0 ? (
          <div className="rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-[12%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Sección</TableHead>
                  <TableHead className="w-[26%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Nombre</TableHead>
                  <TableHead className="w-[22%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Email</TableHead>
                  <TableHead className="w-[12%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Estado</TableHead>
                  <TableHead className="w-[28%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Github</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {Array.from({ length: 4 }).map((_, i) => (
                  <TableRow key={`sk-${i}`}>
                    <TableCell><Skeleton className="h-5 w-16 rounded-full" /></TableCell>
                    <TableCell><Skeleton className="h-4 w-40" /></TableCell>
                    <TableCell><Skeleton className="h-4 w-44" /></TableCell>
                    <TableCell><Skeleton className="h-5 w-16 rounded-full" /></TableCell>
                    <TableCell><Skeleton className="h-8 w-32" /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        ) : (
          <div className="rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-[12%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Sección</TableHead>
                  <TableHead className="w-[26%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Nombre</TableHead>
                  <TableHead className="w-[22%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Email</TableHead>
                  <TableHead className="w-[12%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Estado</TableHead>
                  <TableHead className="w-[28%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Github</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visibleEnrollments.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={5} className="py-10">
                      <div className="flex flex-col items-center gap-2 text-center text-muted-foreground">
                        <Users className="size-8 text-muted-foreground/40" />
                        <p className="text-sm">Sin alumnos en el roster.</p>
                        <p className="text-xs text-muted-foreground/70">Importa un archivo roster JSON para poblar esta sección.</p>
                      </div>
                    </TableCell>
                  </TableRow>
                ) : (
                  visibleEnrollments.map((e) => (
                    <TableRow key={e.id}>
                      <TableCell>
                        <Badge solidColor={BADGE.sectionAlt}>
                          {sectionLabel(e.section_id)}
                        </Badge>
                      </TableCell>
                      <TableCell>{e.full_name}</TableCell>
                      <TableCell className="font-mono text-xs tabular-nums">
                        {e.email ?? (
                          <span className="text-muted-foreground">—</span>
                        )}
                      </TableCell>
                      <TableCell>
                        <Badge solidColor={BADGE.success}>{e.status}</Badge>
                      </TableCell>
                      <TableCell>
                        {editingId === e.id ? (
                          <div className="flex items-center gap-1.5">
                            <Input
                              type="text"
                              value={draftGithub}
                              placeholder="usuario-github"
                              autoFocus
                              className="h-8"
                              onChange={(ev) => setDraftGithub(ev.target.value)}
                              onKeyDown={(ev) => {
                                if (ev.key === "Enter") void commitGithub(e);
                                if (ev.key === "Escape") {
                                  setEditingId(null);
                                  setDraftGithub("");
                                }
                              }}
                            />
                            <Button
                              size="icon-sm"
                              className="bg-emerald-600 text-white hover:bg-emerald-600/90"
                              onClick={() => void commitGithub(e)}
                              aria-label="Guardar github"
                            >
                              <Check className="size-4" />
                            </Button>
                            <Button
                              variant="outline"
                              size="icon-sm"
                              onClick={() => {
                                setEditingId(null);
                                setDraftGithub("");
                              }}
                              aria-label="Cancelar"
                            >
                              <X className="size-4" />
                            </Button>
                          </div>
                        ) : (
                          <div className="flex items-center gap-2">
                            {e.github_username ? (
                              <span className="font-mono">
                                {e.github_username}
                              </span>
                            ) : (
                              <Badge solidColor={BADGE.neutral}>sin github</Badge>
                            )}
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => {
                                setEditingId(e.id);
                                setDraftGithub(e.github_username ?? "");
                              }}
                            >
                              {e.github_username ? "Editar" : "Asignar"}
                            </Button>
                          </div>
                        )}
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </div>
        )}

        {/* -------- Validación / conflictos (vista v_enrollment_status) -------- */}
        <div className="flex flex-col gap-3">
          <h3 className="text-base font-semibold">Validación cruzada</h3>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
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
      </CardContent>
    </Card>
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
    <div className="flex flex-col gap-2 rounded-lg border bg-muted/30 p-3">
      <div className="flex items-center gap-2">
        <strong className="text-sm">{title}</strong>
        <Badge solidColor={color}>{count}</Badge>
      </div>
      {rows.length === 0 ? (
        <p className="text-xs text-muted-foreground">{empty}</p>
      ) : (
        <ul className="m-0 flex flex-col gap-1 pl-4 text-sm">
          {rows.map((r) => (
            <li key={r.key} className="list-disc">
              {r.label}{" "}
              <span className="text-xs text-muted-foreground">· {r.sub}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
