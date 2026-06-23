"use client";

import { useMemo, useState } from "react";
import { toast } from "sonner";
import { LayoutGrid, Plus, RefreshCw, Trash2 } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type { SectionRow } from "@/lib/types";
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

export function SectionsSection() {
  const { rows, loading, error, refresh } = useSections();
  const { rows: courses } = useCourses();
  const [courseId, setCourseId] = useState<string>("");
  const [code, setCode] = useState("");
  const [name, setName] = useState("");

  const courseMap = useMemo(() => {
    const m = new Map<number, string>();
    for (const c of courses) m.set(c.id, c.code);
    return m;
  }, [courses]);

  async function addSection() {
    const cid = Number(courseId);
    const c = code.trim();
    const n = name.trim();
    if (!cid || !c || !n) {
      toast.error("Selecciona curso y completa código y nombre.");
      return;
    }
    const { data, error: err } = await supabase
      .from("sections")
      .insert({ course_id: cid, code: c, name: n })
      .select();
    if (err) {
      const duplicate = err.code === "23505" || /duplicate|unique/i.test(err.message);
      toast.error(duplicate ? `La sección "${c}" ya existe en ese curso.` : "Error: " + err.message);
      return;
    }
    if (!data || data.length === 0) {
      toast.error("No se pudo agregar (¿sesión expirada?).");
      return;
    }
    setCode("");
    setName("");
    toast.success(`Sección "${c}" agregada.`);
    void refresh();
  }

  async function deleteSection(id: SectionRow["id"], code: string) {
    if (!window.confirm(`¿Eliminar la sección "${code}"? Se borrarán sus evaluaciones en cascada.`)) return;
    const { data, error: err } = await supabase.from("sections").delete().eq("id", id).select();
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
    <Card id="sec-sections" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          Secciones
          <span className="inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-muted px-1.5 text-xs font-medium text-muted-foreground">
            {rows.length}
          </span>
        </CardTitle>
        <CardDescription>
          Crea secciones bajo cada curso. Las evaluaciones se asignan a una sección específica.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-6">
        <div className="flex flex-wrap items-end gap-3">
          <div className="grid w-[200px] gap-1.5">
            <Label htmlFor="sectionCourse">Curso</Label>
            <Select value={courseId} onValueChange={setCourseId}>
              <SelectTrigger id="sectionCourse" className="w-full">
                <SelectValue placeholder="Seleccionar curso..." />
              </SelectTrigger>
              <SelectContent>
                {courses.map((c) => (
                  <SelectItem key={c.id} value={String(c.id)}>
                    {c.code} — {c.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="grid w-[120px] gap-1.5">
            <Label htmlFor="sectionCode">Código</Label>
            <Input
              type="text"
              id="sectionCode"
              placeholder="001D"
              value={code}
              onChange={(e) => setCode(e.target.value)}
            />
          </div>
          <div className="grid flex-1 gap-1.5">
            <Label htmlFor="sectionName">Nombre</Label>
            <Input
              type="text"
              id="sectionName"
              placeholder="Sección 001D"
              value={name}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") void addSection(); }}
            />
          </div>
          <Button onClick={addSection}>
            <Plus />
            Agregar sección
          </Button>
        </div>

        <div className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-[20%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Curso</TableHead>
                <TableHead className="w-[20%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Código</TableHead>
                <TableHead className="w-[40%] text-xs font-medium uppercase tracking-wide text-muted-foreground">Nombre</TableHead>
                <TableHead className="w-[20%] text-right text-xs font-medium uppercase tracking-wide text-muted-foreground">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {loading && rows.length === 0 ? (
                Array.from({ length: 3 }).map((_, i) => (
                  <TableRow key={`sk-${i}`}>
                    <TableCell><Skeleton className="h-5 w-16 rounded-md" /></TableCell>
                    <TableCell><Skeleton className="h-4 w-16" /></TableCell>
                    <TableCell><Skeleton className="h-4 w-40" /></TableCell>
                    <TableCell className="text-right"><Skeleton className="ml-auto size-8 rounded-md" /></TableCell>
                  </TableRow>
                ))
              ) : error ? (
                <TableRow>
                  <TableCell colSpan={4} className="text-center text-destructive">
                    Error: {error}
                  </TableCell>
                </TableRow>
              ) : rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={4} className="py-10">
                    <div className="flex flex-col items-center gap-2 text-center text-muted-foreground">
                      <LayoutGrid className="size-8 text-muted-foreground/40" />
                      <p className="text-sm">Sin secciones configuradas.</p>
                      <p className="text-xs text-muted-foreground/70">Crea una sección bajo un curso para asignarle evaluaciones.</p>
                    </div>
                  </TableCell>
                </TableRow>
              ) : (
                rows.map((s) => (
                  <TableRow key={s.id}>
                    <TableCell>
                      <span className="inline-flex items-center rounded-md bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary">
                        {courseMap.get(s.course_id) ?? "?"}
                      </span>
                    </TableCell>
                    <TableCell className="font-mono tabular-nums">{s.code}</TableCell>
                    <TableCell>{s.name}</TableCell>
                    <TableCell className="text-right">
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        className="text-destructive hover:text-destructive"
                        onClick={() => deleteSection(s.id, s.code)}
                        aria-label="Eliminar sección"
                      >
                        <Trash2 />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))
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
