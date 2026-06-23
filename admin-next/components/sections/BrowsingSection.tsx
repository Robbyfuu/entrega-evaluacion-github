"use client";

import { useMemo, useState } from "react";
import { Globe, RefreshCw } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import type { BrowserHistoryRow } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { fmt } from "@/lib/format";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
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

// Historial de navegación (últimos 100). Live via browser_history.
export function BrowsingSection() {
  const [onlyBlocked, setOnlyBlocked] = useState(false);
  const [flashIds, setFlashIds] = useState<Set<string | number>>(new Set());

  const { rows, loading, error, refresh } = useRealtimeTable<
    BrowserHistoryRow & Record<string, unknown>
  >({
    table: "browser_history",
    order: { column: "visited_at", ascending: false },
    limit: 100,
    getId: (r) => r.id ?? `${r.visited_at}|${r.url}`,
    onInsert: (row) => {
      if (row.allowed === false) {
        const id = row.id ?? `${row.visited_at}|${row.url}`;
        setFlashIds((prev) => new Set(prev).add(id));
        setTimeout(() => {
          setFlashIds((prev) => {
            const next = new Set(prev);
            next.delete(id);
            return next;
          });
        }, 1600);
      }
    },
  });

  const { sectionCodeById } = useSectionLookup();

  const visible = useMemo(
    () => (onlyBlocked ? rows.filter((r) => r.allowed === false) : rows),
    [rows, onlyBlocked]
  );

  const blockedCount = useMemo(
    () => rows.filter((r) => r.allowed === false).length,
    [rows]
  );

  return (
    <Card id="sec-browsing" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Globe className="size-5 text-primary" />
          Historial de navegación
          <span className="text-xs font-normal text-muted-foreground">
            (últimos 100)
          </span>
          <span className="ml-auto inline-flex h-6 min-w-6 items-center justify-center rounded-full bg-destructive/10 px-2 text-sm font-semibold text-destructive tabular-nums">
            {blockedCount} bloqueos
          </span>
        </CardTitle>
        <CardDescription>
          Navegación del navegador interno del alumno. Solo se permiten GitHub,
          Microsoft (login) y Google (login/Gmail). Las filas rojas son intentos
          a sitios prohibidos (disparan trampa).
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-wrap items-center gap-4">
          <div className="flex items-center gap-2">
            <Switch
              id="browseFilter"
              checked={onlyBlocked}
              onCheckedChange={setOnlyBlocked}
            />
            <Label htmlFor="browseFilter">Solo bloqueados</Label>
          </div>
          <Button variant="outline" size="sm" onClick={refresh}>
            <RefreshCw className="size-4" />
            Refrescar
          </Button>
        </div>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Fecha</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Usuario</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">PC</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Sección</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Estado</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">URL</TableHead>
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
                  <TableCell><Skeleton className="h-5 w-20 rounded-full" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-52" /></TableCell>
                </TableRow>
              ))
            ) : error ? (
              <TableRow>
                <TableCell colSpan={6} className="py-8 text-center text-destructive">
                  Error: {error}
                </TableCell>
              </TableRow>
            ) : visible.length === 0 ? (
              <TableRow>
                <TableCell colSpan={6} className="py-10">
                  <div className="flex flex-col items-center gap-2 text-center text-muted-foreground">
                    <Globe className="size-8 text-muted-foreground/40" />
                    <p className="text-sm">
                      {onlyBlocked ? "Sin intentos bloqueados." : "Sin navegación registrada."}
                    </p>
                  </div>
                </TableCell>
              </TableRow>
            ) : (
              visible.map((r, i) => {
                const id = r.id ?? `${r.visited_at}|${r.url}`;
                const blocked = r.allowed === false;
                const cls =
                  (blocked ? "row-blocked" : "") +
                  (flashIds.has(id) ? " row-flash" : "");
                return (
                  <TableRow key={r.id ?? i} className={cls.trim() || undefined}>
                    <TableCell className="text-muted-foreground tabular-nums">
                      {fmt(r.visited_at)}
                    </TableCell>
                    <TableCell>
                      {r.github_username ? (
                        <Badge solidColor={BADGE.user}>@{r.github_username}</Badge>
                      ) : (
                        "-"
                      )}
                    </TableCell>
                    <TableCell className="font-medium">{r.pc_name || "-"}</TableCell>
                    <TableCell>
                      {(sectionCodeById(r.section_id) ?? r.section) || "-"}
                    </TableCell>
                    <TableCell>
                      <Badge variant={r.allowed ? "success" : "cheat"}>
                        {r.allowed ? "permitido" : "BLOQUEADO"}
                      </Badge>
                    </TableCell>
                    <TableCell
                      title={r.url || ""}
                      className="max-w-[360px] truncate font-mono text-xs text-muted-foreground"
                    >
                      {r.url || ""}
                    </TableCell>
                  </TableRow>
                );
              })
            )}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}
