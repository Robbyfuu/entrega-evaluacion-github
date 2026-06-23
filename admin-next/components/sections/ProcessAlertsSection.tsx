"use client";

import { useEffect } from "react";
import { RefreshCw, ShieldCheck, TriangleAlert } from "lucide-react";
import type { ProcessAlertRow } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { fmt } from "@/lib/format";
import { BADGE } from "@/lib/colors";
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
import { Skeleton } from "@/components/ui/skeleton";

interface ProcessAlertsSectionProps {
  onCountChange: (count: number) => void;
}

// Alertas de procesos sospechosos (últimas 50). Live via process_alerts.
export function ProcessAlertsSection({ onCountChange }: ProcessAlertsSectionProps) {
  const { rows, loading, error, refresh } = useRealtimeTable<
    ProcessAlertRow & Record<string, unknown>
  >({
    table: "process_alerts",
    order: { column: "detected_at", ascending: false },
    limit: 50,
    getId: (r) => r.id ?? `${r.detected_at}|${r.pc_name}|${r.process_name}`,
  });

  const { sectionCodeById } = useSectionLookup();

  useEffect(() => {
    onCountChange(rows.length);
  }, [rows.length, onCountChange]);

  return (
    <Card id="sec-alerts" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <TriangleAlert className="size-5 text-amber-500" />
          Alertas de procesos sospechosos
          <span className="text-xs font-normal text-muted-foreground">
            (últimas 50)
          </span>
          <span className="ml-auto inline-flex h-6 min-w-6 items-center justify-center rounded-full bg-destructive/10 px-2 text-sm font-semibold text-destructive tabular-nums">
            {rows.length}
          </span>
        </CardTitle>
        <CardDescription>
          Se dispara cuando un alumno abre browsers, mensajeros, IDEs alternos,
          terminales o software de acceso remoto durante la sesión.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Fecha</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Usuario</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">PC</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Sección</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Proceso</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Título ventana</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading && rows.length === 0 ? (
              Array.from({ length: 4 }).map((_, i) => (
                <TableRow key={`sk-${i}`}>
                  <TableCell><Skeleton className="h-4 w-32" /></TableCell>
                  <TableCell><Skeleton className="h-5 w-28 rounded-full" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-24" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-12" /></TableCell>
                  <TableCell><Skeleton className="h-5 w-24 rounded-full" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-44" /></TableCell>
                </TableRow>
              ))
            ) : error ? (
              <TableRow>
                <TableCell colSpan={6} className="py-8 text-center text-destructive">
                  Error: {error}
                </TableCell>
              </TableRow>
            ) : rows.length === 0 ? (
              <TableRow>
                <TableCell colSpan={6} className="py-10">
                  <div className="flex flex-col items-center gap-2 text-center text-muted-foreground">
                    <ShieldCheck className="size-8 text-emerald-500/50" />
                    <p className="text-sm">Sin alertas de procesos.</p>
                    <p className="text-xs text-muted-foreground/70">Ningún alumno ha abierto procesos sospechosos.</p>
                  </div>
                </TableCell>
              </TableRow>
            ) : (
              rows.map((a, i) => (
                <TableRow key={a.id ?? i}>
                  <TableCell className="text-muted-foreground tabular-nums">
                    {fmt(a.detected_at)}
                  </TableCell>
                  <TableCell>
                    {a.github_username ? (
                      <Badge solidColor={BADGE.user}>@{a.github_username}</Badge>
                    ) : (
                      "-"
                    )}
                  </TableCell>
                  <TableCell className="font-medium">{a.pc_name || "-"}</TableCell>
                  <TableCell>
                    {(sectionCodeById(a.section_id) ?? a.section) || "-"}
                  </TableCell>
                  <TableCell>
                    <Badge solidColor={BADGE.danger}>{a.process_name}</Badge>
                  </TableCell>
                  <TableCell className="max-w-[280px] truncate text-muted-foreground">
                    {a.window_title || "-"}
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
        <Button variant="outline" size="sm" onClick={refresh}>
          <RefreshCw className="size-4" />
          Refrescar
        </Button>
      </CardContent>
    </Card>
  );
}
