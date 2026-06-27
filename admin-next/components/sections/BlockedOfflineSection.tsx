"use client";

import { useEffect, useMemo, useState } from "react";
import { WifiOff } from "lucide-react";
import type { OnlineClientRow } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { fmt, timeAgo } from "@/lib/format";
import { ONLINE_WINDOW_MS } from "@/lib/section-workspace";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";
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

interface BlockedOfflineSectionProps {
  onCountChange?: (count: number) => void;
}

// PCs DESCONECTADOS que quedaron con el internet BLOQUEADO: el alumno entregó y
// apagó el PC mientras el bloqueo seguía activo. Su última señal reporta
// internet_state='blocked' y last_seen ya viejo (offline). Sirve para saber qué
// equipos revisar (pueden bootear sin internet hasta que el cliente lo libere).
export function BlockedOfflineSection({ onCountChange }: BlockedOfflineSectionProps) {
  const { rows, loading, error } = useRealtimeTable<OnlineClientRow & Record<string, unknown>>({
    table: "online_clients",
    order: { column: "last_seen", ascending: false },
    limit: 200,
    getId: (r) => `${r.pc_name}|${r.github_username}|${r.evaluation_id ?? 0}`,
  });

  const { sectionCodeById } = useSectionLookup();

  // Tick 10s para recalcular "desconectado" (heartbeat > 90s).
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 10000);
    return () => clearInterval(id);
  }, []);

  const blockedOffline = useMemo(() => {
    const cutoff = now - ONLINE_WINDOW_MS;
    return rows.filter(
      (c) =>
        c.internet_state === "blocked" &&
        new Date(c.last_seen).getTime() < cutoff
    );
  }, [rows, now]);

  useEffect(() => {
    onCountChange?.(blockedOffline.length);
  }, [blockedOffline.length, onCountChange]);

  return (
    <Card id="sec-blocked-offline" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2 text-base">
          <WifiOff className="size-5 text-amber-500" />
          PCs apagados con internet bloqueado
          <span
            className={
              "ml-auto inline-flex h-6 min-w-6 items-center justify-center rounded-full px-2 text-sm font-semibold tabular-nums " +
              (blockedOffline.length > 0
                ? "bg-amber-500/15 text-amber-600 dark:text-amber-400"
                : "bg-emerald-500/10 text-emerald-500")
            }
          >
            {blockedOffline.length}
          </span>
        </CardTitle>
        <CardDescription>
          Equipos desconectados cuya última señal reportó el internet bloqueado
          (el alumno entregó y apagó el PC con el bloqueo activo). Conviene
          revisarlos: pueden quedar sin internet hasta que el cliente lo libere.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <div className="overflow-hidden rounded-lg border">
          <Table>
            <TableHeader className="bg-muted/50">
              <TableRow>
                {["PC", "Alumno", "Sección", "Última señal", "Desconectado"].map((h) => (
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
              {loading && blockedOffline.length === 0 ? (
                Array.from({ length: 3 }).map((_, i) => (
                  <TableRow key={`sk-${i}`}>
                    {Array.from({ length: 5 }).map((__, j) => (
                      <TableCell key={j}><Skeleton className="h-4 w-24" /></TableCell>
                    ))}
                  </TableRow>
                ))
              ) : error ? (
                <TableRow>
                  <TableCell colSpan={5} className="py-8 text-center text-destructive">
                    Error: {error}
                  </TableCell>
                </TableRow>
              ) : blockedOffline.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={5} className="py-10 text-center text-sm text-muted-foreground">
                    Ningún PC apagado quedó con internet bloqueado.
                  </TableCell>
                </TableRow>
              ) : (
                blockedOffline.map((c) => {
                  const section = sectionCodeById(c.section_id) ?? c.section ?? "—";
                  return (
                    <TableRow key={`${c.pc_name}|${c.github_username}`}>
                      <TableCell className="font-medium">{c.pc_name || "—"}</TableCell>
                      <TableCell>
                        {c.github_username ? (
                          <Badge solidColor={BADGE.user}>@{c.github_username}</Badge>
                        ) : (
                          <span className="text-muted-foreground">(sin login)</span>
                        )}
                      </TableCell>
                      <TableCell>
                        {section !== "—" ? (
                          <Badge solidColor={BADGE.sectionAlt}>{section}</Badge>
                        ) : (
                          "—"
                        )}
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground tabular-nums">
                        {fmt(c.last_seen)}
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground tabular-nums">
                        {timeAgo(c.last_seen, now)}
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
