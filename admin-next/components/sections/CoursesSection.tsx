"use client";

import { useState } from "react";
import { toast } from "sonner";
import { Plus, RefreshCw, Trash2 } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type { CourseRow } from "@/lib/types";
import { useCourses } from "@/hooks/useCourses";
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
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

export function CoursesSection() {
  const { rows, loading, error, refresh } = useCourses();
  const [code, setCode] = useState("");
  const [name, setName] = useState("");

  async function addCourse() {
    const c = code.trim();
    const n = name.trim();
    if (!c || !n) {
      toast.error("Completa código y nombre.");
      return;
    }
    const { data, error: err } = await supabase
      .from("courses")
      .insert({ code: c, name: n, active: true })
      .select();
    if (err) {
      const duplicate = err.code === "23505" || /duplicate|unique/i.test(err.message);
      toast.error(duplicate ? `El código "${c}" ya existe.` : "Error: " + err.message);
      return;
    }
    if (!data || data.length === 0) {
      toast.error("No se pudo agregar (¿sesión expirada?).");
      return;
    }
    setCode("");
    setName("");
    toast.success(`Curso "${c}" agregado.`);
    void refresh();
  }

  async function toggleCourse(id: CourseRow["id"], active: boolean) {
    const { data, error: err } = await supabase.from("courses").update({ active }).eq("id", id).select();
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

  async function deleteCourse(id: CourseRow["id"], code: string) {
    if (!window.confirm(`¿Eliminar el curso "${code}"? Se borrarán sus secciones y evaluaciones en cascada.`)) return;
    const { data, error: err } = await supabase.from("courses").delete().eq("id", id).select();
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
    <Card id="sec-courses" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          Cursos
          <span className="inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-muted px-1.5 text-xs font-medium text-muted-foreground">
            {rows.length}
          </span>
        </CardTitle>
        <CardDescription>
          Crea y administra los cursos. Cada curso agrupa secciones y evaluaciones.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-6">
        <div className="flex flex-wrap items-end gap-3">
          <div className="grid w-[140px] gap-1.5">
            <Label htmlFor="courseCode">Código</Label>
            <Input
              type="text"
              id="courseCode"
              placeholder="FPY1101"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") void addCourse(); }}
            />
          </div>
          <div className="grid flex-1 gap-1.5">
            <Label htmlFor="courseName">Nombre</Label>
            <Input
              type="text"
              id="courseName"
              placeholder="Física I"
              value={name}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") void addCourse(); }}
            />
          </div>
          <Button onClick={addCourse}>
            <Plus />
            Agregar curso
          </Button>
        </div>

        <div className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-[20%]">Código</TableHead>
                <TableHead className="w-[40%]">Nombre</TableHead>
                <TableHead className="w-[15%]">Estado</TableHead>
                <TableHead className="w-[25%] text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {loading && rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={4} className="text-center text-muted-foreground">
                    Cargando...
                  </TableCell>
                </TableRow>
              ) : error ? (
                <TableRow>
                  <TableCell colSpan={4} className="text-center text-destructive">
                    Error: {error}
                  </TableCell>
                </TableRow>
              ) : rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={4} className="text-center text-muted-foreground">
                    Sin cursos configurados.
                  </TableCell>
                </TableRow>
              ) : (
                rows.map((c) => (
                  <TableRow key={c.id}>
                    <TableCell className="font-mono">{c.code}</TableCell>
                    <TableCell>{c.name}</TableCell>
                    <TableCell>
                      <span
                        className={
                          c.active
                            ? "inline-flex items-center gap-1.5 rounded-full bg-emerald-500/10 px-2 py-0.5 text-xs font-medium text-emerald-600 dark:text-emerald-400"
                            : "inline-flex items-center gap-1.5 rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground"
                        }
                      >
                        <span className={c.active ? "size-1.5 rounded-full bg-emerald-500" : "size-1.5 rounded-full bg-muted-foreground/50"} />
                        {c.active ? "Activo" : "Inactivo"}
                      </span>
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex justify-end gap-2">
                        <Button
                          variant={c.active ? "outline" : "default"}
                          size="sm"
                          onClick={() => toggleCourse(c.id, !c.active)}
                        >
                          {c.active ? "Desactivar" : "Activar"}
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          className="text-destructive hover:text-destructive"
                          onClick={() => deleteCourse(c.id, c.code)}
                          aria-label="Eliminar curso"
                        >
                          <Trash2 />
                        </Button>
                      </div>
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
