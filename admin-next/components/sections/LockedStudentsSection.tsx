"use client";

import { useEffect, useState } from "react";
import { Globe, LockKeyhole, RotateCcw, ShieldCheck, Unlock } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type { TargetedLockdownRow } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { fmt, timeAgo } from "@/lib/format";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Skeleton } from "@/components/ui/skeleton";

interface LockedStudentsSectionProps {
  onCountChange?: (count: number) => void;
}

// Alumnos en pantalla roja AHORA: lee targeted_lockdowns activos (lockdown
// dirigido del profe + auto-locks reportados por trampas locales del cliente).
// El profe desbloquea desde aca (la liberación es authenticated-only).
export function LockedStudentsSection({ onCountChange }: LockedStudentsSectionProps) {
  const { rows, loading, error, refresh } = useRealtimeTable<
    TargetedLockdownRow & Record<string, unknown>
  >({
    table: "targeted_lockdowns",
    order: { column: "created_at", ascending: false },
    getId: (r) => `${r.pc_name}|${r.github_username}`,
  });

  const locked = rows.filter((r) => r.active);

  // Nombre de PC tipeado a mano: para desbloquear (o reactivar monitoreo de) un
  // PC que quedo trabado y NO aparece en la lista de lockdowns activos.
  const [pcNameInput, setPcNameInput] = useState("");

  useEffect(() => {
    onCountChange?.(locked.length);
  }, [locked.length, onCountChange]);

  // Desbloquea internet + pantalla roja de un PC por nombre de maquina (upsert a
  // pc_overrides). El cliente C# lo aplica en <20s sin importar el usuario.
  // Contrato FIJO de columnas.
  async function unblockPcByName(pcName: string) {
    const name = pcName.trim();
    if (!name) {
      window.alert("Escribe el nombre del PC.");
      return;
    }
    const { error: err } = await supabase.from("pc_overrides").upsert(
      {
        pc_name: name,
        unblock_internet: true,
        unblock_screen: true,
        updated_at: new Date().toISOString(),
      },
      { onConflict: "pc_name" }
    );
    window.alert(
      err
        ? "Error: " + err.message
        : `✓ Internet y pantalla desbloqueados en ${name}. Se aplicará en <20s.`
    );
  }

  // Reactiva el monitoreo de un PC: apaga ambos overrides (UPDATE pc_overrides)
  // para que el PC vuelva a poder bloquearse bajo las reglas normales.
  async function reactivateMonitoring(pcName: string) {
    const name = pcName.trim();
    if (!name) {
      window.alert("Escribe el nombre del PC.");
      return;
    }
    const { error: err } = await supabase
      .from("pc_overrides")
      .update({
        unblock_internet: false,
        unblock_screen: false,
        updated_at: new Date().toISOString(),
      })
      .eq("pc_name", name);
    window.alert(
      err
        ? "Error: " + err.message
        : `✓ Monitoreo reactivado en ${name}. El PC vuelve a poder bloquearse.`
    );
  }

  async function unlock(row: TargetedLockdownRow) {
    if (!window.confirm(`¿Desbloquear a @${row.github_username} (${row.pc_name})?`)) return;
    // .select() para confirmar: si la sesión expiró, RLS rechaza sin lanzar error.
    const { data, error: err } = await supabase
      .from("targeted_lockdowns")
      .update({ active: false, released_at: new Date().toISOString() })
      .match({ pc_name: row.pc_name, github_username: row.github_username })
      .select();
    if (err) {
      window.alert("Error: " + err.message);
      return;
    }
    if (!data || data.length === 0) {
      window.alert("No se pudo desbloquear (¿sesión expirada?). Vuelve a iniciar sesión.");
      return;
    }
    void refresh();
  }

  return (
    <Card id="sec-locked" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2 text-base">
          <LockKeyhole className="size-5 text-destructive" />
          Alumnos bloqueados (pantalla roja)
          <span
            className={
              "ml-auto inline-flex h-6 min-w-6 items-center justify-center rounded-full px-2 text-sm font-semibold tabular-nums " +
              (locked.length > 0
                ? "bg-destructive/10 text-destructive"
                : "bg-emerald-500/10 text-emerald-500")
            }
          >
            {locked.length}
          </span>
        </CardTitle>
        <CardDescription>
          Alumnos con la pantalla roja activa ahora. Incluye los bloqueos manuales
          del profesor y las trampas locales (repo sucio, navegación prohibida).
          Desbloquear los libera en menos de 10s.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <div className="mb-4 flex flex-wrap items-end gap-2 rounded-lg border border-dashed p-3">
          <div className="min-w-[200px] flex-1">
            <Label htmlFor="pc-override-name" className="mb-1 text-xs font-medium uppercase tracking-wide text-muted-foreground">
              Nombre de PC
            </Label>
            <Input
              id="pc-override-name"
              value={pcNameInput}
              onChange={(e) => setPcNameInput(e.target.value)}
              placeholder="Ej: LAB-PC-12"
            />
          </div>
          <Button
            variant="default"
            size="sm"
            title="Desbloquear internet y pantalla del PC tipeado (sirve para un PC que no aparece en la lista)"
            onClick={() => void unblockPcByName(pcNameInput)}
          >
            <Globe className="size-3.5" /> Desbloquear por nombre de PC
          </Button>
          <Button
            variant="outline"
            size="sm"
            title="Apaga los overrides del PC tipeado para que vuelva a poder bloquearse"
            onClick={() => void reactivateMonitoring(pcNameInput)}
          >
            <RotateCcw className="size-3.5" /> Reactivar monitoreo
          </Button>
        </div>
        <div className="overflow-hidden rounded-lg border">
          <Table>
            <TableHeader className="bg-muted/50">
              <TableRow>
                {["Alumno", "PC", "Origen", "Motivo", "Desde", "Acción"].map((h) => (
                  <TableHead
                    key={h}
                    className="text-xs font-medium uppercase tracking-wide text-muted-foreground"
                  >
                    {h}
                  </TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {loading && locked.length === 0 ? (
                Array.from({ length: 3 }).map((_, i) => (
                  <TableRow key={`sk-${i}`}>
                    {Array.from({ length: 6 }).map((__, j) => (
                      <TableCell key={j}><Skeleton className="h-4 w-20" /></TableCell>
                    ))}
                  </TableRow>
                ))
              ) : error ? (
                <TableRow>
                  <TableCell colSpan={6} className="py-8 text-center text-destructive">
                    Error: {error}
                  </TableCell>
                </TableRow>
              ) : locked.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={6} className="py-10">
                    <div className="flex flex-col items-center gap-2 text-center text-muted-foreground">
                      <ShieldCheck className="size-8 text-emerald-500/50" />
                      <p className="text-sm">Ningún alumno bloqueado ahora.</p>
                    </div>
                  </TableCell>
                </TableRow>
              ) : (
                locked.map((r) => (
                  <TableRow key={`${r.pc_name}|${r.github_username}`}>
                    <TableCell>
                      <Badge solidColor={BADGE.user}>@{r.github_username}</Badge>
                    </TableCell>
                    <TableCell className="font-medium">{r.pc_name}</TableCell>
                    <TableCell>
                      <Badge solidColor={r.source === "trap" ? BADGE.danger : BADGE.lockdown}>
                        {r.source === "trap" ? "Trampa" : "Profesor"}
                      </Badge>
                    </TableCell>
                    <TableCell className="max-w-xs truncate text-sm" title={r.reason ?? ""}>
                      {r.reason ?? "—"}
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground tabular-nums">
                      {r.created_at ? `${fmt(r.created_at)} (${timeAgo(r.created_at)})` : "—"}
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-wrap items-center gap-1.5">
                        <Button variant="outline" size="sm" onClick={() => unlock(r)}>
                          <Unlock className="size-3.5" /> Desbloquear
                        </Button>
                        <Button
                          variant="outline"
                          size="sm"
                          title="Desbloquear internet y pantalla de este PC (por nombre de maquina)"
                          onClick={() => void unblockPcByName(r.pc_name)}
                        >
                          <Globe className="size-3.5" /> Desbloquear PC
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </div>
      </CardContent>
    </Card>
  );
}
