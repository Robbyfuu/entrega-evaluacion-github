"use client";

import { useMemo, useState } from "react";
import { toast } from "sonner";
import { ClipboardList, ExternalLink, Plus, RefreshCw, Trash2 } from "lucide-react";
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

export function EvaluationsSection() {
  const { rows, loading, error, refresh } = useEvaluations();
  const { rows: sections } = useSections();
  const { rows: courses } = useCourses();
  const [sectionId, setSectionId] = useState<string>("");
  const [title, setTitle] = useState("");
  const [url, setUrl] = useState("");
  const [org, setOrg] = useState("");

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

  async function addEvaluation() {
    const sid = Number(sectionId);
    const t = title.trim();
    if (!sid || !t) {
      toast.error("Selecciona sección y completa el título.");
      return;
    }
    const { data, error: err } = await supabase
      .from("evaluations")
      .insert({
        section_id: sid,
        title: t,
        classroom_url: url.trim() || null,
        org: org.trim() || null,
        active: false,
      })
      .select();
    if (err) {
      toast.error("Error: " + err.message);
      return;
    }
    if (!data || data.length === 0) {
      toast.error("No se pudo agregar (¿sesión expirada?).");
      return;
    }
    setTitle("");
    setUrl("");
    setOrg("");
    toast.success(`Evaluación "${t}" agregada (inactiva).`);
    void refresh();
  }

  async function toggleEvaluation(id: EvaluationRow["id"], active: boolean) {
    const { data, error: err } = await supabase.from("evaluations").update({ active }).eq("id", id).select();
    if (err) {
      toast.error("Error: " + err.message);
      return;
    }
    if (!data || data.length === 0) {
      toast.error("No se pudo actualizar (¿sesión expirada?).");
      return;
    }
    void refresh();
  }

  async function deleteEvaluation(id: EvaluationRow["id"], title: string) {
    if (!window.confirm(`¿Eliminar la evaluación "${title}"?`)) return;
    const { data, error: err } = await supabase.from("evaluations").delete().eq("id", id).select();
    if (err) {
      toast.error("Error: " + err.message);
      return;
    }
    if (!data || data.length === 0) {
      toast.error("No se pudo eliminar (¿sesión expirada?).");
      return;
    }
    void refresh();
  }

  return (
    <Card id="sec-evaluations" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          Evaluaciones
          <span className="inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-muted px-1.5 text-xs font-medium text-muted-foreground">
            {rows.length}
          </span>
        </CardTitle>
        <CardDescription>
          Crea evaluaciones por sección. Activa la evaluación para que los alumnos la vean al arrancar.
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
            <Label htmlFor="evalUrl">URL Classroom (opcional)</Label>
            <Input
              type="text"
              id="evalUrl"
              placeholder="https://classroom.github.com/a/XXXX"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") void addEvaluation(); }}
            />
          </div>
          <Button onClick={addEvaluation}>
            <Plus />
            Agregar evaluación
          </Button>
        </div>

        <div className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-[25%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Título</TableHead>
                <TableHead className="w-[20%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Sección</TableHead>
                <TableHead className="w-[25%] text-xs font-medium uppercase tracking-wide text-muted-foreground">URL</TableHead>
                <TableHead className="w-[12%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Estado</TableHead>
                <TableHead className="w-[18%] text-right text-xs font-medium uppercase tracking-wide text-muted-foreground">Acciones</TableHead>
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
                      <p className="text-xs text-muted-foreground/70">Crea una evaluación por sección y actívala cuando el alumno deba verla.</p>
                    </div>
                  </TableCell>
                </TableRow>
              ) : (
                rows.map((e) => {
                  const sec = sectionMap.get(e.section_id);
                  const courseCode = sec ? courseMap.get(sec.course_id) ?? "?" : "?";
                  return (
                    <TableRow key={e.id}>
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
                            className="inline-flex max-w-[260px] items-center gap-1 truncate font-mono text-xs text-primary hover:underline"
                          >
                            <ExternalLink className="size-3 shrink-0" />
                            <span className="truncate">{e.classroom_url}</span>
                          </a>
                        ) : (
                          <span className="text-muted-foreground">—</span>
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
                        <div className="flex justify-end gap-2">
                          <Button
                            variant={e.active ? "outline" : "default"}
                            size="sm"
                            onClick={() => toggleEvaluation(e.id, !e.active)}
                          >
                            {e.active ? "Desactivar" : "Activar"}
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            className="text-destructive hover:text-destructive"
                            onClick={() => deleteEvaluation(e.id, e.title)}
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
      </CardContent>
    </Card>
  );
}
