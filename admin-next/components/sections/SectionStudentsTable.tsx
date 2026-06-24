"use client";

import { useMemo, useState } from "react";
import { CheckCircle2, Circle, MonitorOff, Search } from "lucide-react";
import type { UnifiedStudent } from "@/lib/section-workspace";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

interface SectionStudentsTableProps {
  students: UnifiedStudent[];
  loading: boolean;
  error: string | null;
  onSelectStudent: (student: UnifiedStudent) => void;
}

type StatusFilter = "all" | "online" | "pending" | "suspicious";

const FILTERS: { key: StatusFilter; label: string }[] = [
  { key: "all", label: "Todos" },
  { key: "online", label: "Online" },
  { key: "pending", label: "No entregaron" },
  { key: "suspicious", label: "Sospechosos" },
];

function YesNo({ value, yesColor }: { value: boolean; yesColor: string }) {
  return value ? (
    <span className="inline-flex items-center gap-1 text-xs font-medium" style={{ color: yesColor }}>
      <CheckCircle2 className="size-3.5" /> Sí
    </span>
  ) : (
    <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
      <Circle className="size-3" /> No
    </span>
  );
}

export function SectionStudentsTable({
  students,
  loading,
  error,
  onSelectStudent,
}: SectionStudentsTableProps) {
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<StatusFilter>("all");

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return students.filter((s) => {
      if (q) {
        const hay = `${s.fullName ?? ""} ${s.github ?? ""}`.toLowerCase();
        if (!hay.includes(q)) return false;
      }
      if (filter === "online") return s.online;
      if (filter === "pending") return !s.submitted;
      if (filter === "suspicious") return s.suspCount > 0;
      return true;
    });
  }, [students, query, filter]);

  const onlineCount = students.filter((s) => s.online).length;
  const submittedCount = students.filter((s) => s.submitted).length;

  return (
    <div className="flex flex-col gap-3">
      {/* Toolbar: búsqueda + filtros rápidos + resumen */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative w-full sm:max-w-xs">
          <Search className="absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Buscar alumno o github…"
            className="pl-8"
          />
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          {FILTERS.map((f) => (
            <Button
              key={f.key}
              size="sm"
              variant={filter === f.key ? "default" : "outline"}
              onClick={() => setFilter(f.key)}
            >
              {f.label}
            </Button>
          ))}
        </div>
      </div>

      <p className="text-xs text-muted-foreground tabular-nums">
        {filtered.length} de {students.length} alumnos · {onlineCount} online · {submittedCount} entregaron
      </p>

      <div className="overflow-hidden rounded-lg border">
        <Table>
          <TableHeader className="sticky top-0 z-10 bg-muted/50 backdrop-blur">
            <TableRow>
              {["Alumno", "GitHub", "Online", "Aceptó", "Entregó", "Versión", "Procesos", "Lockdown"].map(
                (h) => (
                  <TableHead
                    key={h}
                    className="text-xs font-medium uppercase tracking-wide text-muted-foreground"
                  >
                    {h}
                  </TableHead>
                )
              )}
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading && students.length === 0 ? (
              Array.from({ length: 6 }).map((_, i) => (
                <TableRow key={`sk-${i}`}>
                  {Array.from({ length: 8 }).map((__, j) => (
                    <TableCell key={j}><Skeleton className="h-4 w-20" /></TableCell>
                  ))}
                </TableRow>
              ))
            ) : error ? (
              <TableRow>
                <TableCell colSpan={8} className="py-8 text-center text-destructive">
                  Error: {error}
                </TableCell>
              </TableRow>
            ) : filtered.length === 0 ? (
              <TableRow>
                <TableCell colSpan={8} className="py-10">
                  <div className="flex flex-col items-center gap-2 text-center text-muted-foreground">
                    <MonitorOff className="size-8 text-muted-foreground/40" />
                    <p className="text-sm">
                      {students.length === 0
                        ? "Sin alumnos en esta sección todavía."
                        : "Ningún alumno coincide con el filtro."}
                    </p>
                  </div>
                </TableCell>
              </TableRow>
            ) : (
              filtered.map((s) => (
                <TableRow
                  key={s.key}
                  className={cn(
                    "cursor-pointer transition-colors hover:bg-muted/50",
                    !s.enrolled && "bg-amber-500/5"
                  )}
                  onClick={() => onSelectStudent(s)}
                >
                  <TableCell className="font-medium">
                    {s.fullName ?? (
                      <span className="italic text-muted-foreground">fuera de roster</span>
                    )}
                  </TableCell>
                  <TableCell>
                    {s.github ? (
                      <Badge solidColor={BADGE.user}>@{s.github}</Badge>
                    ) : (
                      <span className="text-xs text-muted-foreground">(sin github)</span>
                    )}
                  </TableCell>
                  <TableCell>
                    <span className="inline-flex items-center gap-1.5">
                      <span
                        className={cn(
                          "size-2 rounded-full",
                          s.online ? "bg-emerald-500" : "bg-muted-foreground/30"
                        )}
                      />
                      <span className="text-xs text-muted-foreground">
                        {s.online ? "online" : "offline"}
                      </span>
                    </span>
                  </TableCell>
                  <TableCell><YesNo value={s.accepted} yesColor={BADGE.success} /></TableCell>
                  <TableCell><YesNo value={s.submitted} yesColor={BADGE.user} /></TableCell>
                  <TableCell className="text-xs tabular-nums text-muted-foreground">
                    {s.version ? `v${s.version}` : "—"}
                  </TableCell>
                  <TableCell>
                    {s.online ? (
                      s.suspCount > 0 ? (
                        <Badge solidColor={BADGE.danger}>⚠ {s.suspCount}</Badge>
                      ) : (
                        <span className="text-xs tabular-nums text-muted-foreground">
                          {Array.isArray(s.client?.processes) ? s.client!.processes!.length : 0}
                        </span>
                      )
                    ) : (
                      <span className="text-xs text-muted-foreground">—</span>
                    )}
                  </TableCell>
                  <TableCell>
                    {s.lockdown ? (
                      <Badge solidColor={BADGE.lockdown}>ACTIVO</Badge>
                    ) : (
                      <span className="text-xs text-muted-foreground">no</span>
                    )}
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}
