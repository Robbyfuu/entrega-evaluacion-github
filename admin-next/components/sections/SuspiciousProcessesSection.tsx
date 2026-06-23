"use client";

import { useMemo, useState } from "react";
import { CheckCircle2, RefreshCw, Trash2, XCircle } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type { SuspiciousProcess } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { normalizeProcessName } from "@/lib/suspicious";
import { fmt } from "@/lib/format";
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
import { cn } from "@/lib/utils";

const GLOBAL_LABEL = "Global (todas las secciones)";

// Sentinel for the radix Select item mapped to the empty section ("" = global).
const GLOBAL_VALUE = "__global__";

// Procesos sospechosos: editor CRUD sobre la tabla `suspicious_processes`.
// El cliente C# y el panel comparten esta tabla en tiempo real, así el profesor
// edita la lista y los equipos la aplican sin reiniciar.
export function SuspiciousProcessesSection() {
  const { rows, loading, error, refresh } = useRealtimeTable<
    SuspiciousProcess & Record<string, unknown>
  >({
    table: "suspicious_processes",
    order: { column: "process_name", ascending: true },
    getId: (r) => r.id,
  });

  const { sections } = useSectionLookup();

  const [feedback, setFeedback] = useState<{ text: string; ok: boolean } | null>(null);
  const [name, setName] = useState("");
  const [section, setSection] = useState<string>("");

  // Section codes come from the DB (dynamic) plus any extra value seen in
  // existing rows, so the editor never hides a row even if its section was
  // deleted from the sections table.
  const sectionCodes = useMemo(() => {
    const codes = new Set<string>();
    for (const s of sections) codes.add(s.code);
    for (const r of rows) if (r.section) codes.add(r.section);
    return Array.from(codes).sort();
  }, [sections, rows]);

  // Group rows: a "Global" bucket (section === null) first, then one bucket per
  // known section.
  const groups = useMemo(() => {
    const global = rows.filter((r) => r.section === null);
    const bySection = sectionCodes.map((sec) => ({
      key: sec,
      label: sec,
      items: rows.filter((r) => r.section === sec),
    }));

    return [{ key: "__global__", label: GLOBAL_LABEL, items: global }, ...bySection];
  }, [rows, sectionCodes]);

  async function addProcess() {
    const normalized = normalizeProcessName(name);
    if (!normalized) {
      setFeedback({ text: "Escribe el nombre del proceso.", ok: false });
      return;
    }
    // .select() para confirmar que la fila se escribio: si la sesion expiro,
    // RLS rechaza el write sin lanzar error (data vacia, error null). Sin esta
    // verificacion el profe veria "agregado" mientras NADA se escribio: falso
    // negativo de proctoring. data vacia => tratamos como error de permisos.
    const { data, error: err } = await supabase
      .from("suspicious_processes")
      .insert({
        process_name: normalized,
        section: section === "" ? null : section,
      })
      .select();
    if (err) {
      // 23505 = unique_violation. Surface a friendly message for duplicates.
      const duplicate = err.code === "23505" || /duplicate|unique/i.test(err.message);
      setFeedback({
        text: duplicate
          ? `"${normalized}" ya está en la lista para esa sección.`
          : "Error: " + err.message,
        ok: false,
      });
      return;
    }
    if (!data || data.length === 0) {
      setFeedback({
        text: "No se pudo agregar (¿sesión expirada?). Vuelve a iniciar sesión.",
        ok: false,
      });
      return;
    }
    setName("");
    setFeedback({ text: `Proceso "${normalized}" agregado.`, ok: true });
    void refresh();
  }

  async function deleteProcess(id: SuspiciousProcess["id"]) {
    if (!window.confirm("¿Eliminar este proceso de la lista?")) return;
    // .select() para confirmar el borrado: misma razon que en addProcess, si
    // RLS rechaza por sesion expirada no se lanza error pero no se borra nada.
    const { data, error: err } = await supabase
      .from("suspicious_processes")
      .delete()
      .eq("id", id)
      .select();
    if (err) {
      setFeedback({ text: "Error: " + err.message, ok: false });
      return;
    }
    if (!data || data.length === 0) {
      setFeedback({
        text: "No se pudo eliminar (¿sesión expirada?). Vuelve a iniciar sesión.",
        ok: false,
      });
      return;
    }
    void refresh();
  }

  const total = rows.length;

  return (
    <Card id="sec-suspicious" className="mb-4 scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2">
          Procesos sospechosos
          <span className="text-xs font-normal text-muted-foreground">
            (resaltan en PCs conectados y alertas)
          </span>
          <Badge variant="neutral">{total}</Badge>
        </CardTitle>
        <CardDescription>
          Lista de programas que se marcan como sospechosos durante el examen. Usa{" "}
          <strong className="font-semibold text-foreground">Global</strong> para
          todas las secciones, o limita un proceso a una sección. Los equipos la
          aplican en tiempo real.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        {/* Alta de proceso */}
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <div className="flex flex-1 flex-col gap-1.5">
            <Label htmlFor="suspName">Nombre del proceso</Label>
            <Input
              type="text"
              id="suspName"
              placeholder="Nombre del proceso (ej: chrome, code, discord)"
              value={name}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") void addProcess();
              }}
            />
          </div>
          <div className="flex w-full flex-col gap-1.5 sm:w-56">
            <Label htmlFor="suspSection">Sección</Label>
            <Select
              value={section === "" ? GLOBAL_VALUE : section}
              onValueChange={(value) =>
                setSection(value === GLOBAL_VALUE ? "" : value)
              }
            >
              <SelectTrigger id="suspSection" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={GLOBAL_VALUE}>{GLOBAL_LABEL}</SelectItem>
                {sectionCodes.map((sec) => (
                  <SelectItem key={sec} value={sec}>
                    {sec}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button onClick={addProcess}>Agregar proceso</Button>
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

        {loading && rows.length === 0 ? (
          <p className="text-sm text-muted-foreground">Cargando...</p>
        ) : error ? (
          <p className="text-sm text-destructive">Error: {error}</p>
        ) : (
          <div className="flex flex-col gap-6">
            {groups.map((group) => (
              <div key={group.key} className="flex flex-col gap-2">
                <div className="flex items-center gap-2">
                  <Badge
                    solidColor={
                      group.key === "__global__" ? BADGE.sectionAlt : BADGE.user
                    }
                  >
                    {group.label}
                  </Badge>
                  <span className="text-xs text-muted-foreground">
                    {group.items.length}
                  </span>
                </div>
                {group.items.length === 0 ? (
                  <p className="text-sm text-muted-foreground">
                    Sin procesos en este grupo.
                  </p>
                ) : (
                  <div className="rounded-lg border">
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead className="w-1/2">
                            Nombre del proceso
                          </TableHead>
                          <TableHead className="w-[35%]">Agregado</TableHead>
                          <TableHead className="w-[15%] text-right">
                            Acciones
                          </TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {group.items.map((p) => (
                          <TableRow key={p.id}>
                            <TableCell className="font-mono">
                              {p.process_name}
                            </TableCell>
                            <TableCell className="text-xs text-muted-foreground">
                              {fmt(p.created_at)}
                            </TableCell>
                            <TableCell className="text-right">
                              <Button
                                variant="ghost"
                                size="icon-sm"
                                className="text-destructive hover:bg-destructive/10 hover:text-destructive"
                                onClick={() => deleteProcess(p.id)}
                                aria-label="Eliminar proceso"
                              >
                                <Trash2 className="size-4" />
                              </Button>
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}

        <div>
          <Button variant="outline" size="sm" onClick={refresh}>
            <RefreshCw className="size-4" />
            Refrescar
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
