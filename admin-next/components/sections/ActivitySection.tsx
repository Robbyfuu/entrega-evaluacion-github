"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Activity, ExternalLink, Inbox, RefreshCw } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type { StudentActivityRow } from "@/lib/types";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { fmt } from "@/lib/format";
import { ACTION_LABEL, ACTION_COLOR, BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
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
import { Skeleton } from "@/components/ui/skeleton";
import { safeHref } from "@/lib/url";

// Sentinel value for the "all" option in shadcn Select (it cannot hold "").
const ALL = "__all__";

// Actividad de alumnos (últimos 100), filtered by action and section.
// Uses manual fetch on filter change / Refrescar (realtime optional here).
export function ActivitySection() {
  const { sections, sectionCodeById } = useSectionLookup();
  const [actionFilter, setActionFilter] = useState("");
  const [sectionFilter, setSectionFilter] = useState("");
  const [rows, setRows] = useState<StudentActivityRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const sectionCodes = useMemo(() => {
    const codes = new Set<string>();
    for (const s of sections) codes.add(s.code);
    for (const r of rows) if (r.section) codes.add(r.section);
    return Array.from(codes).sort();
  }, [sections, rows]);

  // Resuelve el section_id correspondiente al code seleccionado para el
  // filtro OR (filas legacy con section TEXT + filas migradas con section_id).
  const sectionFilterId = useMemo(() => {
    if (!sectionFilter) return null;
    const s = sections.find((x) => x.code === sectionFilter);
    return s?.id ?? null;
  }, [sectionFilter, sections]);

  const load = useCallback(async () => {
    setLoading(true);
    let q = supabase
      .from("student_activity")
      .select("*")
      .order("created_at", { ascending: false })
      .limit(100);
    if (actionFilter) q = q.eq("action", actionFilter);
    if (sectionFilter) {
      // Filtro OR: cubre filas legacy (section TEXT) y migradas (section_id).
      // Si no hay section_id resuelto, cae solo a section TEXT.
      if (sectionFilterId != null) {
        q = q.or(`section.eq.${sectionFilter},section_id.eq.${sectionFilterId}`);
      } else {
        q = q.eq("section", sectionFilter);
      }
    }
    const { data, error: err } = await q;
    if (err) {
      setError(err.message);
      setRows([]);
    } else {
      setError(null);
      setRows((data ?? []) as StudentActivityRow[]);
    }
    setLoading(false);
  }, [actionFilter, sectionFilter]);

  useEffect(() => {
    void load();
  }, [load]);

  const sectionLabel = (r: StudentActivityRow) =>
    sectionCodeById(r.section_id) ?? r.section ?? null;

  return (
    <Card id="sec-activity" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Activity className="size-5 text-primary" />
          Actividad de alumnos
          <span className="text-xs font-normal text-muted-foreground">
            (últimos 100)
          </span>
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-wrap items-end gap-3">
          <div className="grid gap-1.5">
            <Label htmlFor="actionFilter">Filtrar por acción</Label>
            <Select
              value={actionFilter === "" ? ALL : actionFilter}
              onValueChange={(v) => setActionFilter(v === ALL ? "" : v)}
            >
              <SelectTrigger id="actionFilter" className="w-[180px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>Todas</SelectItem>
                <SelectItem value="login">Login</SelectItem>
                <SelectItem value="create_repo">Crear repo</SelectItem>
                <SelectItem value="clone">Clonar repo</SelectItem>
                <SelectItem value="upload">Subir archivos</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="sectionFilter">Filtrar por sección</Label>
            <Select
              value={sectionFilter === "" ? ALL : sectionFilter}
              onValueChange={(v) => setSectionFilter(v === ALL ? "" : v)}
            >
              <SelectTrigger id="sectionFilter" className="w-[160px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>Todas</SelectItem>
                {sectionCodes.map((sec) => (
                  <SelectItem key={sec} value={sec}>
                    {sec}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button variant="outline" size="sm" onClick={load}>
            <RefreshCw className="size-4" />
            Refrescar
          </Button>
        </div>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Fecha</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Sección</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Usuario GitHub</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Email</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">PC</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Acción</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Repo</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading && rows.length === 0 ? (
              Array.from({ length: 4 }).map((_, i) => (
                <TableRow key={`sk-${i}`}>
                  <TableCell><Skeleton className="h-4 w-32" /></TableCell>
                  <TableCell><Skeleton className="h-5 w-14 rounded-full" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-24" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-40" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-20" /></TableCell>
                  <TableCell><Skeleton className="h-5 w-16 rounded-full" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-32" /></TableCell>
                </TableRow>
              ))
            ) : error ? (
              <TableRow>
                <TableCell colSpan={7} className="py-8 text-center text-destructive">
                  Error: {error}
                </TableCell>
              </TableRow>
            ) : rows.length === 0 ? (
              <TableRow>
                <TableCell colSpan={7} className="py-10">
                  <div className="flex flex-col items-center gap-2 text-center text-muted-foreground">
                    <Inbox className="size-8 text-muted-foreground/40" />
                    <p className="text-sm">Sin actividad registrada.</p>
                    <p className="text-xs text-muted-foreground/70">No hay eventos para los filtros seleccionados.</p>
                  </div>
                </TableCell>
              </TableRow>
            ) : (
              rows.map((e, i) => {
                const sec = sectionLabel(e);
                return (
                  <TableRow key={e.id ?? i}>
                    <TableCell className="text-muted-foreground tabular-nums">
                      {fmt(e.created_at)}
                    </TableCell>
                    <TableCell>
                      {sec ? <Badge solidColor={BADGE.user}>{sec}</Badge> : "-"}
                    </TableCell>
                    <TableCell className="font-medium">
                      @{e.github_username || "?"}
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      {e.github_email || "-"}
                    </TableCell>
                    <TableCell>{e.pc_name || "-"}</TableCell>
                    <TableCell>
                      <Badge solidColor={ACTION_COLOR[e.action] || "#999"}>
                        {ACTION_LABEL[e.action] || e.action}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      {e.repo_url ? (
                        <a
                          href={safeHref(e.repo_url) ?? undefined}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="inline-flex items-center gap-1 text-primary hover:underline"
                        >
                          {e.repo_name || e.repo_url}
                          <ExternalLink className="size-3" />
                        </a>
                      ) : (
                        e.repo_name || "-"
                      )}
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
