"use client";

import { useMemo, useState } from "react";
import { toast } from "sonner";
import { ClipboardList, ExternalLink, Pencil, Plus, RefreshCw, Trash2, Users, X } from "lucide-react";
import { EvaluationStudentsDrawer } from "@/components/sections/EvaluationStudentsDrawer";
import { supabase } from "@/lib/supabase";
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
  const [editingId, setEditingId] = useState<number | null>(null);
  const [detailEval, setDetailEval] = useState<EvaluationRow | null>(null);

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
  }

  function startEdit(e: EvaluationRow) {
    setEditingId(e.id);
    setSectionId(String(e.section_id));
    setTitle(e.title);
    setUrl(e.classroom_url ?? "");
    setOrg(e.org ?? "");
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

    if (editingId != null) {
      // EDITAR evaluación existente (incluye añadir/cambiar el link de Classroom).
      const { data, error: err } = await supabase
        .from("evaluations")
        .update({ section_id: sid, title: t, classroom_url: classroomUrl || null, org: orgValue || null })
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
      .insert({ section_id: sid, title: t, classroom_url: classroomUrl || null, org: orgValue || null, active: false })
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

  const editing = editingId != null;

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

        <div className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-[22%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Título</TableHead>
                <TableHead className="w-[18%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Sección</TableHead>
                <TableHead className="w-[28%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Link Classroom</TableHead>
                <TableHead className="w-[12%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Estado</TableHead>
                <TableHead className="w-[20%] text-right text-xs font-medium uppercase tracking-wide text-muted-foreground">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {loading && rows.length === 0 ? (
                Array.from({ length: 3 }).map((_, i) => (
                  <TableRow key={`sk-${i}`}>
                    <TableCell><Skeleton className="h-4 w-32" /></TableCell>
                    <TableCell><Skeleton className="h-5 w-20 rounded-md" /></TableCell>
                    <TableCell><Skeleton className="h-4 w-44" /></TableCell>
                    <TableCell><Skeleton className="h-5 w-16 rounded-full" /></TableCell>
                    <TableCell className="text-right"><Skeleton className="ml-auto h-8 w-24" /></TableCell>
                  </TableRow>
                ))
              ) : error ? (
                <TableRow>
                  <TableCell colSpan={5} className="text-center text-destructive">
                    Error: {error}
                  </TableCell>
                </TableRow>
              ) : rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={5} className="py-10">
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
                      <TableCell className="font-medium">{e.title}</TableCell>
                      <TableCell>
                        <span className="inline-flex items-center rounded-md bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary">
                          {courseCode} / {sec?.code ?? "?"}
                        </span>
                      </TableCell>
                      <TableCell>
                        {e.classroom_url ? (
                          <a
                            href={e.classroom_url}
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
