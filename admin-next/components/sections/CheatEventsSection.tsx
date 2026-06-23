"use client";

import { useState } from "react";
import { RefreshCw, ShieldAlert } from "lucide-react";
import type { CheatEventRow } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { fmt } from "@/lib/format";
import { playAlertBeep } from "@/lib/sound";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/button";
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

// Eventos de trampa detectados (últimos 50). Live via cheat_events with a
// visual flash + sound cue when a new event arrives.
export function CheatEventsSection() {
  const [flashIds, setFlashIds] = useState<Set<string | number>>(new Set());

  const { rows, loading, error, refresh } = useRealtimeTable<
    CheatEventRow & Record<string, unknown>
  >({
    table: "cheat_events",
    order: { column: "detected_at", ascending: false },
    limit: 50,
    getId: (r) => r.id ?? `${r.detected_at}|${r.pc_name}|${r.username}`,
    onInsert: (row) => {
      const id = row.id ?? `${row.detected_at}|${row.pc_name}|${row.username}`;
      playAlertBeep();
      setFlashIds((prev) => new Set(prev).add(id));
      setTimeout(() => {
        setFlashIds((prev) => {
          const next = new Set(prev);
          next.delete(id);
          return next;
        });
      }, 1600);
    },
  });

  return (
    <Card id="sec-cheat" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <ShieldAlert className="size-5 text-destructive" />
          Eventos de trampa detectados
          <span className="text-xs font-normal text-muted-foreground">
            (últimos 50)
          </span>
        </CardTitle>
        <CardDescription>
          Detecciones automáticas de manipulación de archivos o repositorios
          durante la sesión.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Fecha</TableHead>
              <TableHead>Usuario GitHub</TableHead>
              <TableHead>PC</TableHead>
              <TableHead>Repo</TableHead>
              <TableHead>Archivos</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading && rows.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={5}
                  className="py-8 text-center text-muted-foreground"
                >
                  Cargando...
                </TableCell>
              </TableRow>
            ) : error ? (
              <TableRow>
                <TableCell colSpan={5} className="py-8 text-center text-destructive">
                  Error: {error}
                </TableCell>
              </TableRow>
            ) : rows.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={5}
                  className="py-8 text-center text-muted-foreground"
                >
                  Sin eventos de trampa registrados.
                </TableCell>
              </TableRow>
            ) : (
              rows.map((e, i) => {
                const id = e.id ?? `${e.detected_at}|${e.pc_name}|${e.username}`;
                const sample = (e.files_sample ?? []).slice(0, 5).join(", ");
                return (
                  <TableRow
                    key={e.id ?? i}
                    className={flashIds.has(id) ? "row-flash" : undefined}
                  >
                    <TableCell className="text-muted-foreground">
                      {fmt(e.detected_at)}
                    </TableCell>
                    <TableCell>
                      <Badge variant="cheat">{e.username || "(?)"}</Badge>
                    </TableCell>
                    <TableCell className="font-medium">{e.pc_name || "-"}</TableCell>
                    <TableCell>
                      <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs">
                        {e.repo_name || "-"}
                      </code>
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      <span className="font-medium text-foreground">
                        {e.files_count}
                      </span>
                      {sample ? `: ${sample}` : ""}
                    </TableCell>
                  </TableRow>
                );
              })
            )}
          </TableBody>
        </Table>
        <Button variant="outline" size="sm" onClick={refresh}>
          <RefreshCw className="size-4" />
          Refrescar eventos
        </Button>
      </CardContent>
    </Card>
  );
}
