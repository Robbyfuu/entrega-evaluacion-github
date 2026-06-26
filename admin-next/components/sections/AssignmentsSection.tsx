"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import { ClipboardList, ExternalLink, Plus, Trash2 } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type { AssignmentRow, AssignmentAcceptanceRow, AssignmentSubmissionRow, EvaluationRow } from "@/lib/types";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { useEvaluations } from "@/hooks/useEvaluations";
import { useEnrollmentCounts } from "@/hooks/useEnrollmentCounts";
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
import { Switch } from "@/components/ui/switch";
import { safeHref } from "@/lib/url";
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

// Sentinel values for the Select UI: Radix Select cannot use "" as an item
// value, so the empty-string state ("all sections" / "no evaluation") is mapped
// to these constants at the presentation layer only. The underlying state keeps
// "" exactly as before.
const ALL_SECTIONS = "__all__";
const NO_EVALUATION = "__none__";

// Tareas de GitHub Classroom: assignments CRUD + acceptance cross-reference.
export function AssignmentsSection() {
  const [rows, setRows] = useState<AssignmentRow[]>([]);
  const [acceptances, setAcceptances] = useState<AssignmentAcceptanceRow[]>([]);
  const [submissions, setSubmissions] = useState<AssignmentSubmissionRow[]>([]);

  const [title, setTitle] = useState("");
  const [section, setSection] = useState("");
  const [org, setOrg] = useState("");
  const [url, setUrl] = useState("");
  const [evaluationId, setEvaluationId] = useState<string>("");
  const [manualSubmission, setManualSubmission] = useState(false);

  const { sections, sectionById, courseById } = useSectionLookup();
  const { rows: evaluations } = useEvaluations();
  const { countForSection } = useEnrollmentCounts();

  // Section codes come from the DB (dynamic) plus any extra value seen in
  // existing rows, so the editor never hides a row.
  const sectionCodes = useMemo(() => {
    const codes = new Set<string>();
    for (const s of sections) codes.add(s.code);
    for (const r of rows) if (r.section) codes.add(r.section);
    return Array.from(codes).sort();
  }, [sections, rows]);

  // Evaluations grouped by section_id for cascading select.
  const evaluationsBySection = useMemo(() => {
    const m = new Map<number, EvaluationRow[]>();
    for (const e of evaluations) {
      const arr = m.get(e.section_id) ?? [];
      arr.push(e);
      m.set(e.section_id, arr);
    }
    return m;
  }, [evaluations]);

  // When section changes, reset evaluationId if not valid for that section.
  // Si la evaluacion seleccionada desaparece del realtime feed (deleted por
  // otro admin), limpia evaluationId para evitar persistir un ID stale.
  useEffect(() => {
    if (!evaluationId) return;
    const ev = evaluations.find((e) => String(e.id) === evaluationId);
    if (!ev) {
      // La evaluacion ya no existe: limpiar para evitar FK violation.
      setEvaluationId("");
      return;
    }
    // Si la evaluacion sigue existiendo pero su seccion no matchea la
    // seleccionada, actualizar la seccion para reflejar herencia (curso/
    // seccion vienen de la evaluacion).
    const sec = sectionById.get(ev.section_id);
    if (sec && sec.code !== section) {
      setSection(sec.code);
    }
  }, [evaluationId, evaluations, sectionById, section]);

  const load = useCallback(async () => {
    const { data } = await supabase
      .from("assignments")
      .select("*")
      .order("created_at", { ascending: false });
    setRows((data ?? []) as AssignmentRow[]);

    // Cross-reference acceptances to show how many students accepted each task.
    const { data: acc } = await supabase.from("assignment_acceptances").select("*");
    setAcceptances((acc ?? []) as AssignmentAcceptanceRow[]);

    // Cross-reference submissions (formal deliveries) per task.
    const { data: subs } = await supabase.from("assignment_submissions").select("*");
    setSubmissions((subs ?? []) as AssignmentSubmissionRow[]);
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const acceptanceCount = useMemo(() => {
    const map = new Map<string, number>();
    for (const a of acceptances) {
      const key = String(a.assignment_id);
      map.set(key, (map.get(key) ?? 0) + 1);
    }
    return map;
  }, [acceptances]);

  const submissionCount = useMemo(() => {
    const map = new Map<string, number>();
    for (const s of submissions) {
      const key = String(s.assignment_id);
      map.set(key, (map.get(key) ?? 0) + 1);
    }
    return map;
  }, [submissions]);

  const sectionLabel = (r: AssignmentRow) => {
    if (r.evaluation_id) {
      const ev = evaluations.find((e) => e.id === r.evaluation_id);
      if (ev) {
        const sec = sectionById.get(ev.section_id);
        if (sec) {
          const course = courseById.get(sec.course_id);
          return `${course?.code ?? "?"}/${sec.code}`;
        }
      }
    }
    return r.section || "Todas";
  };

  // Resolves the section_id an assignment belongs to: prefer its evaluation's
  // section, fall back to its section TEXT code -> sections lookup. Returns null
  // for "all sections" assignments (no roster denominator applies).
  const sectionIdForAssignment = (r: AssignmentRow): number | null => {
    if (r.evaluation_id) {
      const ev = evaluations.find((e) => e.id === r.evaluation_id);
      if (ev) return ev.section_id;
    }
    if (r.section) {
      const sec = sections.find((s) => s.code === r.section);
      if (sec) return sec.id;
    }
    return null;
  };

  // Renders "X de N" when the assignment maps to a section with a roster,
  // otherwise just "X" (no enrolled denominator for all-sections tasks).
  const tally = (count: number, sectionId: number | null) => {
    const denom = countForSection(sectionId);
    return denom && denom > 0 ? `${count} de ${denom}` : String(count);
  };

  async function addAssignment() {
    if (!title.trim() || (!url.trim() && !manualSubmission)) {
      toast.error("Completa título y al menos URL o entrega manual.");
      return;
    }
    const insert: Record<string, unknown> = {
      title: title.trim(),
      classroom_url: url.trim() || null,
      section,
      org: org.trim() || null,
      active: true,
      allows_manual_submission: manualSubmission,
    };
    if (evaluationId) {
      insert.evaluation_id = Number(evaluationId);
    }
    const { error: err } = await supabase.from("assignments").insert(insert);
    if (err) {
      toast.error("Error: " + err.message);
    } else {
      setTitle("");
      setUrl("");
      setOrg("");
      setEvaluationId("");
      setManualSubmission(false);
      toast.success("Tarea agregada.");
      void load();
    }
  }

  async function toggleAssignment(id: AssignmentRow["id"], active: boolean) {
    await supabase.from("assignments").update({ active }).eq("id", id);
    void load();
  }

  async function deleteAssignment(id: AssignmentRow["id"]) {
    if (!window.confirm("¿Eliminar esta tarea?")) return;
    await supabase.from("assignments").delete().eq("id", id);
    void load();
  }

  // Evaluations available for the currently selected section.
  const availableEvaluations = useMemo(() => {
    if (!section) return evaluations;
    const sec = sections.find((s) => s.code === section);
    if (!sec) return [];
    return evaluationsBySection.get(sec.id) ?? [];
  }, [section, sections, evaluations, evaluationsBySection]);

  return (
    <Card id="sec-tareas" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex flex-wrap items-baseline gap-2 text-lg">
          Tareas de GitHub Classroom
          <span className="text-xs font-normal text-muted-foreground">
            (visibles para los alumnos en su script)
          </span>
        </CardTitle>
        <CardDescription>
          Pega los links de assignment de{" "}
          <a
            href="https://classroom.github.com/"
            target="_blank"
            rel="noopener noreferrer"
            className="text-primary hover:underline"
          >
            classroom.github.com
          </a>
          . Opcional: vincula la tarea a una evaluación para heredar sección y curso.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-6">
        <div className="flex flex-wrap items-end gap-3">
          <div className="grid w-[180px] gap-1.5">
            <Label htmlFor="asgTitle">Título</Label>
            <Input
              type="text"
              id="asgTitle"
              placeholder="Evaluación 1"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
            />
          </div>
          <div className="grid w-[150px] gap-1.5">
            <Label htmlFor="asgSection">Sección</Label>
            <Select
              value={section === "" ? ALL_SECTIONS : section}
              onValueChange={(v) => setSection(v === ALL_SECTIONS ? "" : v)}
            >
              <SelectTrigger id="asgSection" className="w-full">
                <SelectValue placeholder="Todas las secciones" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL_SECTIONS}>Todas las secciones</SelectItem>
                {sectionCodes.map((sec) => (
                  <SelectItem key={sec} value={sec}>
                    {sec}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="grid w-[200px] gap-1.5">
            <Label htmlFor="asgEvaluation">Evaluación (opcional)</Label>
            <Select
              value={evaluationId === "" ? NO_EVALUATION : evaluationId}
              onValueChange={(v) => setEvaluationId(v === NO_EVALUATION ? "" : v)}
            >
              <SelectTrigger id="asgEvaluation" className="w-full">
                <SelectValue placeholder="— Ninguna —" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_EVALUATION}>— Ninguna —</SelectItem>
                {availableEvaluations.map((ev) => {
                  const sec = sectionById.get(ev.section_id);
                  const course = sec ? courseById.get(sec.course_id) : undefined;
                  const label = [course?.code, sec?.code, ev.title]
                    .filter(Boolean)
                    .join(" / ");
                  return (
                    <SelectItem key={ev.id} value={String(ev.id)}>
                      {label}
                    </SelectItem>
                  );
                })}
              </SelectContent>
            </Select>
          </div>
          <div className="grid w-[220px] gap-1.5">
            <Label htmlFor="asgOrg">Org GitHub (Classroom)</Label>
            <Input
              type="text"
              id="asgOrg"
              placeholder="Fundamentos-de-la-Programacion"
              value={org}
              onChange={(e) => setOrg(e.target.value)}
            />
          </div>
          <div className="grid flex-1 gap-1.5">
            <Label htmlFor="asgUrl">URL del Classroom assignment</Label>
            <Input
              type="text"
              id="asgUrl"
              placeholder="https://classroom.github.com/a/XXXX"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
            />
          </div>
          <div className="flex h-9 items-center gap-2">
            <Switch
              id="asgManual"
              checked={manualSubmission}
              onCheckedChange={setManualSubmission}
            />
            <Label htmlFor="asgManual" className="cursor-pointer">Entrega manual</Label>
          </div>
          <Button onClick={addAssignment}>
            <Plus />
            Agregar tarea
          </Button>
        </div>

        <div className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-[18%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Título</TableHead>
                <TableHead className="w-[12%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Sección</TableHead>
                <TableHead className="w-[22%] text-xs font-medium uppercase tracking-wide text-muted-foreground">URL</TableHead>
                <TableHead className="w-[8%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Acept.</TableHead>
                <TableHead className="w-[8%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Entr.</TableHead>
                <TableHead className="w-[12%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Estado</TableHead>
                <TableHead className="w-[15%] text-right text-xs font-medium uppercase tracking-wide text-muted-foreground">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={7} className="py-10">
                    <div className="flex flex-col items-center gap-2 text-center text-muted-foreground">
                      <ClipboardList className="size-8 text-muted-foreground/40" />
                      <p className="text-sm">Sin tareas configuradas.</p>
                      <p className="text-xs text-muted-foreground/70">Agrega una pegando el link de un assignment de Classroom.</p>
                    </div>
                  </TableCell>
                </TableRow>
              ) : (
                rows.map((a) => {
                  const label = sectionLabel(a);
                  const secId = sectionIdForAssignment(a);
                  const accepted = acceptanceCount.get(String(a.id)) ?? 0;
                  const submitted = submissionCount.get(String(a.id)) ?? 0;
                  return (
                    <TableRow key={a.id}>
                      <TableCell className="font-medium">
                        {a.title}
                        {a.allows_manual_submission ? (
                          <span className="ml-1.5 inline-flex items-center rounded bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                            manual
                          </span>
                        ) : null}
                      </TableCell>
                      <TableCell>
                        <span
                          className={
                            a.section || a.evaluation_id
                              ? "inline-flex items-center rounded-md bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary"
                              : "inline-flex items-center rounded-md bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground"
                          }
                        >
                          {label}
                        </span>
                      </TableCell>
                      <TableCell>
                        {a.classroom_url ? (
                          <a
                            href={safeHref(a.classroom_url) ?? undefined}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="inline-flex max-w-[240px] items-center gap-1 truncate font-mono text-xs text-primary hover:underline"
                          >
                            <ExternalLink className="size-3 shrink-0" />
                            <span className="truncate">{a.classroom_url}</span>
                          </a>
                        ) : (
                          <span className="text-xs text-muted-foreground">—</span>
                        )}
                      </TableCell>
                      <TableCell className="font-mono text-sm tabular-nums">{tally(accepted, secId)}</TableCell>
                      <TableCell className="font-mono text-sm tabular-nums">{tally(submitted, secId)}</TableCell>
                      <TableCell>
                        <span
                          className={
                            a.active
                              ? "inline-flex items-center gap-1.5 rounded-full bg-emerald-500/10 px-2 py-0.5 text-xs font-medium text-emerald-600 dark:text-emerald-400"
                              : "inline-flex items-center gap-1.5 rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground"
                          }
                        >
                          <span className={a.active ? "size-1.5 rounded-full bg-emerald-500" : "size-1.5 rounded-full bg-muted-foreground/50"} />
                          {a.active ? "Activa" : "Inactiva"}
                        </span>
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-2">
                          <Button
                            variant={a.active ? "outline" : "default"}
                            size="sm"
                            onClick={() => toggleAssignment(a.id, !a.active)}
                          >
                            {a.active ? "Desactivar" : "Activar"}
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            className="text-destructive hover:text-destructive"
                            onClick={() => deleteAssignment(a.id)}
                            aria-label="Eliminar tarea"
                          >
                            <Trash2 />
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })
              )}
            </TableBody>
          </Table>
        </div>
      </CardContent>
    </Card>
  );
}
