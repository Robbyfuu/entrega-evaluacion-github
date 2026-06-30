"use client";

import { useMemo, useRef, useState } from "react";
import { toast } from "sonner";
import { ClipboardList, ExternalLink, FileText, Pencil, Plus, RefreshCw, Trash2, Upload, Users, X } from "lucide-react";
import { EvaluationStudentsDrawer } from "@/components/sections/EvaluationStudentsDrawer";
import { supabase } from "@/lib/supabase";
import { EXAM_MODES } from "@/lib/types";
import type { EvaluationRow, SectionRow } from "@/lib/types";
import { useEvaluations } from "@/hooks/useEvaluations";
import { useSections } from "@/hooks/useSections";
import { useCourses } from "@/hooks/useCourses";
import { Skeleton } from "@/components/ui/skeleton";
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
import { safeHref } from "@/lib/url";
import { fmt } from "@/lib/format";
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

// `<input type="datetime-local">` produce/consume hora LOCAL de pared SIN
// offset (ej "2026-07-01T14:30"), pero la DB guarda UTC absoluto (timestamptz).
// Este helper toma el ISO UTC de la DB y arma el valor datetime-local en hora
// LOCAL del navegador. OJO: NO se puede cortar el string ISO (mostraría UTC,
// corrido por el offset); hay que leer los componentes LOCALES del Date.
function toDatetimeLocalValue(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  const pad = (n: number) => String(n).padStart(2, "0");
  return (
    `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}` +
    `T${pad(d.getHours())}:${pad(d.getMinutes())}`
  );
}

// Evaluaciones = el ÚNICO lugar para la evaluación Y su tarea de Classroom.
// Antes estaban partidas en "Evaluaciones" + "Tareas Classroom" y se
// desincronizaban (eval sin tarea, tarea apuntando a eval muerta). Ahora la
// evaluación lleva su link de Classroom y, por detrás, mantenemos sincronizada
// la fila de `assignments` (que es lo que LEE el cliente del alumno).
export function EvaluationsSection() {
  const { rows, loading, error, refresh } = useEvaluations();
  const { rows: sections } = useSections();
  const { rows: courses } = useCourses();
  const [sectionId, setSectionId] = useState<string>("");
  const [title, setTitle] = useState("");
  const [url, setUrl] = useState("");
  const [org, setOrg] = useState("");
  const [examMode, setExamMode] = useState<string>("Off");
  // datetime-local en hora LOCAL de pared ("YYYY-MM-DDTHH:mm"). "" => sin término.
  const [endsAt, setEndsAt] = useState("");
  // Numero de la evaluacion (handle estable por seccion). El panel lo asigna
  // explicitamente; NUNCA se infiere del titulo (UNIQUE(section_id, number)).
  const [num, setNum] = useState("");
  const [editingId, setEditingId] = useState<number | null>(null);
  const [detailEval, setDetailEval] = useState<EvaluationRow | null>(null);
  const pdfInputRef = useRef<HTMLInputElement>(null);
  const [pdfBusy, setPdfBusy] = useState(false);

  const sectionMap = useMemo(() => {
    const m = new Map<number, SectionRow>();
    for (const s of sections) m.set(s.id, s);
    return m;
  }, [sections]);

  const courseMap = useMemo(() => {
    const m = new Map<number, string>();
    for (const c of courses) m.set(c.id, c.code);
    return m;
  }, [courses]);

  function resetForm() {
    setEditingId(null);
    setTitle("");
    setUrl("");
    setOrg("");
    setSectionId("");
    setExamMode("Off");
    setEndsAt("");
    setNum("");
  }

  function startEdit(e: EvaluationRow) {
    setEditingId(e.id);
    setSectionId(String(e.section_id));
    setTitle(e.title);
    setUrl(e.classroom_url ?? "");
    setOrg(e.org ?? "");
    setExamMode(e.exam_mode ?? "Off");
    // UTC (DB) -> hora LOCAL para el input; null/"" => campo vacío.
    setEndsAt(e.ends_at ? toDatetimeLocalValue(e.ends_at) : "");
    setNum(e.number != null ? String(e.number) : "");
  }

  // Mantiene sincronizada la `assignment` (lo que lee el cliente) con la
  // evaluación. Con link => upsert de la tarea; sin link => si existe, la
  // desactiva (no hay tarea Classroom que mostrar). El cliente cruza por
  // evaluation_id / sección.
  async function syncAssignment(
    evalId: number,
    sectionCode: string,
    t: string,
    classroomUrl: string,
    orgValue: string,
    active: boolean
  ) {
    const { data: existing } = await supabase
      .from("assignments")
      .select("id")
      .eq("evaluation_id", evalId)
      .limit(1);
    const has = !!existing && existing.length > 0;

    if (classroomUrl) {
      if (has) {
        await supabase
          .from("assignments")
          .update({ title: t, section: sectionCode, classroom_url: classroomUrl, org: orgValue || "", active })
          .eq("evaluation_id", evalId);
      } else {
        await supabase.from("assignments").insert({
          title: t,
          section: sectionCode,
          classroom_url: classroomUrl,
          org: orgValue || "",
          evaluation_id: evalId,
          active,
        });
      }
    } else if (has) {
      // Sin link: no puede haber tarea visible -> desactivar la existente.
      await supabase.from("assignments").update({ active: false }).eq("evaluation_id", evalId);
    }
  }

  async function saveEvaluation() {
    const sid = Number(sectionId);
    const t = title.trim();
    if (!sid || !t) {
      toast.error("Selecciona sección y completa el título.");
      return;
    }
    const sectionCode = sectionMap.get(sid)?.code ?? "";
    const classroomUrl = url.trim();
    const orgValue = org.trim();
    // Hora LOCAL del input -> UTC absoluto para la DB. new Date("YYYY-MM-DDTHH:mm")
    // parsea como hora LOCAL y .toISOString() emite UTC. "" => null (sin término).
    const endsAtIso = endsAt ? new Date(endsAt).toISOString() : null;
    // Numero de la evaluacion: "" => null; debe ser entero positivo. El UNIQUE
    // (section_id, number) en la DB rechaza duplicados dentro de la seccion.
    const numVal = num.trim() === "" ? null : Number(num);
    if (numVal !== null && (!Number.isInteger(numVal) || numVal < 1)) {
      toast.error("El número de evaluación debe ser un entero positivo.");
      return;
    }

    if (editingId != null) {
      // EDITAR evaluación existente (incluye añadir/cambiar el link de Classroom).
      const { data, error: err } = await supabase
        .from("evaluations")
        .update({ section_id: sid, title: t, classroom_url: classroomUrl || null, org: orgValue || null, exam_mode: examMode, ends_at: endsAtIso, number: numVal })
        .eq("id", editingId)
        .select();
      if (err) { toast.error("Error: " + err.message); return; }
      if (!data || data.length === 0) { toast.error("No se pudo guardar (¿sesión expirada?)."); return; }
      // mantener la tarea sincronizada con el estado actual de la evaluación
      await syncAssignment(editingId, sectionCode, t, classroomUrl, orgValue, data[0].active ?? false);
      toast.success(`Evaluación "${t}" actualizada.`);
      resetForm();
      void refresh();
      return;
    }

    // CREAR (inactiva). Se activa con el botón Activar.
    const { data, error: err } = await supabase
      .from("evaluations")
      .insert({ section_id: sid, title: t, classroom_url: classroomUrl || null, org: orgValue || null, exam_mode: examMode, ends_at: endsAtIso, number: numVal, active: false })
      .select();
    if (err) { toast.error("Error: " + err.message); return; }
    if (!data || data.length === 0) { toast.error("No se pudo agregar (¿sesión expirada?)."); return; }
    await syncAssignment(data[0].id, sectionCode, t, classroomUrl, orgValue, false);
    toast.success(`Evaluación "${t}" agregada (inactiva).`);
    resetForm();
    void refresh();
  }

  async function toggleEvaluation(e: EvaluationRow, active: boolean) {
    const { data, error: err } = await supabase.from("evaluations").update({ active }).eq("id", e.id).select();
    if (err) { toast.error("Error: " + err.message); return; }
    if (!data || data.length === 0) { toast.error("No se pudo actualizar (¿sesión expirada?)."); return; }
    // La tarea del cliente sigue el estado de la evaluación.
    const sectionCode = sectionMap.get(e.section_id)?.code ?? "";
    await syncAssignment(e.id, sectionCode, e.title, e.classroom_url ?? "", e.org ?? "", active);
    void refresh();
  }

  async function deleteEvaluation(e: EvaluationRow) {
    if (!window.confirm(`¿Eliminar la evaluación "${e.title}"?`)) return;
    // Desactivar primero su tarea (no la borramos: puede tener aceptaciones/
    // entregas asociadas) y luego eliminar la evaluación.
    await supabase.from("assignments").update({ active: false }).eq("evaluation_id", e.id);
    const { data, error: err } = await supabase.from("evaluations").delete().eq("id", e.id).select();
    if (err) { toast.error("Error: " + err.message); return; }
    if (!data || data.length === 0) { toast.error("No se pudo eliminar (¿sesión expirada?)."); return; }
    if (editingId === e.id) resetForm();
    void refresh();
  }

  // Sube/reemplaza el PDF de enunciado de la evaluacion en edicion. El path es
  // estable por id (`eval-<id>.pdf`) con upsert, asi que reemplazar no deja
  // huerfanos. Tras subir, guarda el path en evaluations.exam_pdf_path.
  async function uploadPdf(file: File) {
    if (editingId == null) return;
    const path = `eval-${editingId}.pdf`;
    setPdfBusy(true);
    const { error: upErr } = await supabase.storage
      .from("exam-pdfs")
      .upload(path, file, { upsert: true, contentType: "application/pdf" });
    if (upErr) { toast.error("Error al subir el PDF: " + upErr.message); setPdfBusy(false); return; }
    const { error: dbErr } = await supabase
      .from("evaluations")
      .update({ exam_pdf_path: path })
      .eq("id", editingId);
    if (dbErr) { toast.error("Error al asociar el PDF: " + dbErr.message); setPdfBusy(false); return; }
    toast.success("PDF de enunciado asociado.");
    setPdfBusy(false);
    void refresh();
  }

  // Borra el objeto del bucket y limpia exam_pdf_path en la evaluacion.
  async function removePdf() {
    if (editingId == null) return;
    const path = `eval-${editingId}.pdf`;
    setPdfBusy(true);
    const { error: rmErr } = await supabase.storage.from("exam-pdfs").remove([path]);
    if (rmErr) { toast.error("Error al quitar el PDF: " + rmErr.message); setPdfBusy(false); return; }
    const { error: dbErr } = await supabase
      .from("evaluations")
      .update({ exam_pdf_path: null })
      .eq("id", editingId);
    if (dbErr) { toast.error("Error al actualizar: " + dbErr.message); setPdfBusy(false); return; }
    toast.success("PDF de enunciado quitado.");
    setPdfBusy(false);
    void refresh();
  }

  const editing = editingId != null;
  // PDF actual de la evaluacion en edicion (se lee de la fila ya cargada).
  const editingPdfPath =
    editingId != null ? rows.find((r) => r.id === editingId)?.exam_pdf_path ?? null : null;

  return (
    <Card id="sec-evaluations" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          Evaluaciones y tareas
          <span className="inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-muted px-1.5 text-xs font-medium text-muted-foreground">
            {rows.length}
          </span>
        </CardTitle>
        <CardDescription>
          Cada evaluación es una tarea de Classroom. Ponle el <strong className="font-semibold text-foreground">link de Classroom</strong>{" "}
          (podés editarlo después) y actívala para que los alumnos la vean. El programa del alumno la toma de aquí.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-6">
        <div className="flex flex-wrap items-end gap-3">
          <div className="grid w-[200px] gap-1.5">
            <Label htmlFor="evalSection">Sección</Label>
            <Select value={sectionId} onValueChange={setSectionId}>
              <SelectTrigger id="evalSection" className="w-full">
                <SelectValue placeholder="Seleccionar sección..." />
              </SelectTrigger>
              <SelectContent>
                {sections.map((s) => (
                  <SelectItem key={s.id} value={String(s.id)}>
                    {courseMap.get(s.course_id) ?? "?"} / {s.code}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="grid w-[180px] gap-1.5">
            <Label htmlFor="evalTitle">Título</Label>
            <Input
              type="text"
              id="evalTitle"
              placeholder="Evaluación 1"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
            />
          </div>
          <div className="grid w-[220px] gap-1.5">
            <Label htmlFor="evalOrg">Org GitHub (opcional)</Label>
            <Input
              type="text"
              id="evalOrg"
              placeholder="Fundamentos-de-la-Programacion"
              value={org}
              onChange={(e) => setOrg(e.target.value)}
            />
          </div>
          <div className="grid w-[110px] gap-1.5">
            <Label htmlFor="evalNum">N° evaluación</Label>
            <Input
              type="number"
              id="evalNum"
              min={1}
              placeholder="ej. 4"
              value={num}
              onChange={(e) => setNum(e.target.value)}
            />
          </div>
          <div className="grid w-[180px] gap-1.5">
            <Label htmlFor="evalExamMode">Modo evaluación</Label>
            <Select value={examMode} onValueChange={setExamMode}>
              <SelectTrigger id="evalExamMode" className="w-full">
                <SelectValue placeholder="Modo evaluación..." />
              </SelectTrigger>
              <SelectContent>
                {EXAM_MODES.map((m) => (
                  <SelectItem key={m} value={m}>
                    {m}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="grid w-[210px] gap-1.5">
            <Label htmlFor="evalEndsAt">Hora de término (opcional)</Label>
            <Input
              type="datetime-local"
              id="evalEndsAt"
              value={endsAt}
              onChange={(e) => setEndsAt(e.target.value)}
            />
          </div>
          <div className="grid flex-1 gap-1.5">
            <Label htmlFor="evalUrl">Link de Classroom</Label>
            <Input
              type="text"
              id="evalUrl"
              placeholder="https://classroom.github.com/a/XXXX"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") void saveEvaluation(); }}
            />
          </div>
          <Button onClick={saveEvaluation}>
            {editing ? <Pencil /> : <Plus />}
            {editing ? "Guardar cambios" : "Agregar evaluación"}
          </Button>
          {editing ? (
            <Button variant="outline" onClick={resetForm}>
              <X />
              Cancelar
            </Button>
          ) : null}
        </div>

        {editing ? (
          <div className="flex flex-wrap items-center gap-3 rounded-md border bg-muted/30 px-3 py-2">
            <input
              ref={pdfInputRef}
              type="file"
              accept="application/pdf"
              className="hidden"
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (file) void uploadPdf(file);
                e.target.value = "";
              }}
            />
            <div className="flex items-center gap-2 text-sm">
              <FileText className="size-4 text-muted-foreground" />
              <span className="font-medium">PDF de enunciado</span>
              {editingPdfPath ? (
                <span className="inline-flex items-center rounded-md bg-primary/10 px-2 py-0.5 font-mono text-xs font-medium text-primary">
                  {editingPdfPath}
                </span>
              ) : (
                <span className="text-xs text-muted-foreground">Sin PDF asociado</span>
              )}
            </div>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={pdfBusy}
              onClick={() => pdfInputRef.current?.click()}
            >
              <Upload />
              {editingPdfPath ? "Reemplazar PDF" : "Subir PDF"}
            </Button>
            {editingPdfPath ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="text-destructive hover:text-destructive"
                disabled={pdfBusy}
                onClick={() => void removePdf()}
              >
                <Trash2 />
                Quitar PDF
              </Button>
            ) : null}
          </div>
        ) : null}

        {examMode !== "Off" ? (
          <p className="rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-700 dark:text-amber-400">
            Modo evaluación <strong>{examMode}</strong>: creá la tarea de GitHub Classroom con
            repositorio <strong>privado</strong> (calificada). Las prácticas pueden ir públicas.
          </p>
        ) : null}

        <div className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-[18%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Título</TableHead>
                <TableHead className="w-[14%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Sección</TableHead>
                <TableHead className="w-[22%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Link Classroom</TableHead>
                <TableHead className="w-[8%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Modo</TableHead>
                <TableHead className="w-[12%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Término</TableHead>
                <TableHead className="w-[10%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Estado</TableHead>
                <TableHead className="w-[16%] text-right text-xs font-medium uppercase tracking-wide text-muted-foreground">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {loading && rows.length === 0 ? (
                Array.from({ length: 3 }).map((_, i) => (
                  <TableRow key={`sk-${i}`}>
                    <TableCell><Skeleton className="h-4 w-32" /></TableCell>
                    <TableCell><Skeleton className="h-5 w-20 rounded-md" /></TableCell>
                    <TableCell><Skeleton className="h-4 w-44" /></TableCell>
                    <TableCell><Skeleton className="h-5 w-16 rounded-md" /></TableCell>
                    <TableCell><Skeleton className="h-4 w-24" /></TableCell>
                    <TableCell><Skeleton className="h-5 w-16 rounded-full" /></TableCell>
                    <TableCell className="text-right"><Skeleton className="ml-auto h-8 w-24" /></TableCell>
                  </TableRow>
                ))
              ) : error ? (
                <TableRow>
                  <TableCell colSpan={7} className="text-center text-destructive">
                    Error: {error}
                  </TableCell>
                </TableRow>
              ) : rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={7} className="py-10">
                    <div className="flex flex-col items-center gap-2 text-center text-muted-foreground">
                      <ClipboardList className="size-8 text-muted-foreground/40" />
                      <p className="text-sm">Sin evaluaciones configuradas.</p>
                      <p className="text-xs text-muted-foreground/70">Crea una evaluación por sección, ponle el link de Classroom y actívala.</p>
                    </div>
                  </TableCell>
                </TableRow>
              ) : (
                rows.map((e) => {
                  const sec = sectionMap.get(e.section_id);
                  const courseCode = sec ? courseMap.get(sec.course_id) ?? "?" : "?";
                  return (
                    <TableRow key={e.id} className={editingId === e.id ? "bg-primary/5" : undefined}>
                      <TableCell className="font-medium">
                        <div className="flex items-center gap-2">
                          <span>{e.title}</span>
                          {e.exam_pdf_path ? (
                            <span
                              className="inline-flex items-center gap-1 rounded-md bg-primary/10 px-1.5 py-0.5 text-[10px] font-medium text-primary"
                              title={`Enunciado: ${e.exam_pdf_path}`}
                            >
                              <FileText className="size-3" />
                              PDF
                            </span>
                          ) : null}
                        </div>
                      </TableCell>
                      <TableCell>
                        <span className="inline-flex items-center rounded-md bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary">
                          {courseCode} / {sec?.code ?? "?"}
                        </span>
                      </TableCell>
                      <TableCell>
                        {e.classroom_url ? (
                          <a
                            href={safeHref(e.classroom_url) ?? undefined}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="inline-flex max-w-[280px] items-center gap-1 truncate font-mono text-xs text-primary hover:underline"
                          >
                            <ExternalLink className="size-3 shrink-0" />
                            <span className="truncate">{e.classroom_url}</span>
                          </a>
                        ) : (
                          <span className="text-xs text-amber-600 dark:text-amber-400">Falta el link — Editar</span>
                        )}
                      </TableCell>
                      <TableCell>
                        {(() => {
                          const mode = e.exam_mode ?? "Off";
                          const isOff = mode === "Off";
                          return (
                            <span
                              className={
                                isOff
                                  ? "inline-flex items-center rounded-md bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground"
                                  : "inline-flex items-center rounded-md bg-amber-500/10 px-2 py-0.5 text-xs font-medium text-amber-600 dark:text-amber-400"
                              }
                            >
                              {mode}
                            </span>
                          );
                        })()}
                      </TableCell>
                      <TableCell>
                        {e.ends_at ? (
                          <span className="text-xs text-foreground">{fmt(e.ends_at)}</span>
                        ) : (
                          <span className="text-xs text-muted-foreground">—</span>
                        )}
                      </TableCell>
                      <TableCell>
                        <span
                          className={
                            e.active
                              ? "inline-flex items-center gap-1.5 rounded-full bg-emerald-500/10 px-2 py-0.5 text-xs font-medium text-emerald-600 dark:text-emerald-400"
                              : "inline-flex items-center gap-1.5 rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground"
                          }
                        >
                          <span className={e.active ? "size-1.5 rounded-full bg-emerald-500" : "size-1.5 rounded-full bg-muted-foreground/50"} />
                          {e.active ? "Activa" : "Inactiva"}
                        </span>
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-1.5">
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => setDetailEval(e)}
                          >
                            <Users className="size-3.5" />
                            Ver alumnos
                          </Button>
                          <Button
                            variant={e.active ? "outline" : "default"}
                            size="sm"
                            onClick={() => toggleEvaluation(e, !e.active)}
                          >
                            {e.active ? "Desactivar" : "Activar"}
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            title="Editar (link, título, sección)"
                            onClick={() => startEdit(e)}
                            aria-label="Editar evaluación"
                          >
                            <Pencil />
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            className="text-destructive hover:text-destructive"
                            onClick={() => deleteEvaluation(e)}
                            aria-label="Eliminar evaluación"
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

        <Button variant="outline" size="sm" onClick={refresh}>
          <RefreshCw />
          Refrescar
        </Button>

        <EvaluationStudentsDrawer
          evaluation={detailEval}
          sectionLabel={(() => {
            if (!detailEval) return "";
            const sec = sectionMap.get(detailEval.section_id);
            const cc = sec ? courseMap.get(sec.course_id) ?? "?" : "?";
            return `${cc} / ${sec?.code ?? "?"}`;
          })()}
          onClose={() => setDetailEval(null)}
        />
      </CardContent>
    </Card>
  );
}
