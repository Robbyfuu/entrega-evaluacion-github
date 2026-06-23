"use client";

import { CheckCircle2, Circle, MonitorOff } from "lucide-react";
import type { UnifiedStudent } from "@/lib/section-workspace";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";
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

// Si/No con icono, para aceptó/entregó.
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
  return (
    <div className="rounded-lg border">
      <Table>
        <TableHeader>
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
            Array.from({ length: 5 }).map((_, i) => (
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
          ) : students.length === 0 ? (
            <TableRow>
              <TableCell colSpan={8} className="py-10">
                <div className="flex flex-col items-center gap-2 text-center text-muted-foreground">
                  <MonitorOff className="size-8 text-muted-foreground/40" />
                  <p className="text-sm">Sin alumnos en esta sección todavía.</p>
                </div>
              </TableCell>
            </TableRow>
          ) : (
            students.map((s) => (
              <TableRow
                key={s.key}
                className={cn("cursor-pointer", !s.enrolled && "bg-amber-500/5")}
                onClick={() => onSelectStudent(s)}
              >
                <TableCell className="font-medium">
                  {s.fullName ?? <span className="text-muted-foreground italic">fuera de roster</span>}
                </TableCell>
                <TableCell>
                  {s.github ? (
                    <Badge solidColor={BADGE.user}>@{s.github}</Badge>
                  ) : (
                    <span className="text-xs text-muted-foreground">(sin github)</span>
                  )}
                </TableCell>
                <TableCell>
                  <Badge solidColor={s.online ? BADGE.success : BADGE.neutral}>
                    {s.online ? "Online" : "Offline"}
                  </Badge>
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
                      <span className="text-xs text-muted-foreground tabular-nums">
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
  );
}
