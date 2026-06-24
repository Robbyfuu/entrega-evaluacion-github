"use client";

import { ChevronRight, ShieldAlert, Users, Wifi } from "lucide-react";
import type { SectionRow } from "@/lib/types";
import type { SectionStats } from "@/lib/section-workspace";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/Badge";
import { BADGE } from "@/lib/colors";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

// Celda KPI compacta (numero prominente + label chico) para las tarjetas.
function Kpi({
  icon,
  value,
  label,
  accent,
}: {
  icon?: React.ReactNode;
  value: React.ReactNode;
  label: string;
  accent?: string;
}) {
  return (
    <div className="flex flex-col items-center justify-center gap-0.5 rounded-md bg-muted/40 py-2">
      <div className={cn("text-lg font-bold leading-none tabular-nums", accent)}>{value}</div>
      <div className="flex items-center gap-1 text-[11px] text-muted-foreground">
        {icon}
        {label}
      </div>
    </div>
  );
}

interface SectionsOverviewProps {
  sections: SectionRow[];
  stats: Map<number, SectionStats>;
  courseCodeBySectionId: (id: number | null | undefined) => string | null;
  activeEvalTitle: (sectionId: number) => string | null;
  loading: boolean;
  onSelectSection: (id: number) => void;
}

export function SectionsOverview({
  sections,
  stats,
  courseCodeBySectionId,
  activeEvalTitle,
  loading,
  onSelectSection,
}: SectionsOverviewProps) {
  if (loading && sections.length === 0) {
    return (
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <Card key={i}><CardContent className="p-5"><Skeleton className="h-28 w-full" /></CardContent></Card>
        ))}
      </div>
    );
  }

  if (sections.length === 0) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          No hay secciones. Creá una en la pestaña Secciones.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {sections.map((sec) => {
        const st = stats.get(sec.id) ?? {
          online: 0,
          enrolled: null,
          accepted: 0,
          submitted: 0,
          suspicious: 0,
        };
        const evalTitle = activeEvalTitle(sec.id);
        const course = courseCodeBySectionId(sec.id);
        return (
          <Card
            key={sec.id}
            className={cn(
              "cursor-pointer overflow-hidden transition-all hover:-translate-y-0.5 hover:border-primary/50 hover:shadow-md",
              st.suspicious > 0 && "border-destructive/40"
            )}
            onClick={() => onSelectSection(sec.id)}
          >
            <CardContent className="flex flex-col gap-4 p-5">
              <div className="flex items-start justify-between">
                <div className="flex items-center gap-2">
                  <div className="text-xl font-bold tracking-tight">{sec.code}</div>
                  {course ? (
                    <span className="rounded bg-muted px-1.5 py-0.5 text-[11px] font-medium text-muted-foreground">
                      {course}
                    </span>
                  ) : null}
                </div>
                <div className="flex items-center gap-2">
                  {st.suspicious > 0 ? (
                    <Badge solidColor={BADGE.danger}>
                      <ShieldAlert className="size-3" /> {st.suspicious}
                    </Badge>
                  ) : null}
                  <ChevronRight className="size-5 text-muted-foreground" />
                </div>
              </div>

              {/* KPI grid data-dense */}
              <div className="grid grid-cols-4 gap-2">
                <Kpi
                  icon={<Wifi className="size-3.5 text-emerald-500" />}
                  value={st.online}
                  label="online"
                  accent="text-emerald-600 dark:text-emerald-400"
                />
                <Kpi
                  icon={<Users className="size-3.5 text-muted-foreground" />}
                  value={st.enrolled ?? "—"}
                  label="roster"
                />
                <Kpi value={st.accepted} label="aceptó" />
                <Kpi value={st.submitted} label="entregó" />
              </div>

              <div className="border-t pt-3 text-xs">
                <span className="text-muted-foreground">Evaluación activa: </span>
                {evalTitle ? (
                  <span className="font-medium text-foreground">{evalTitle}</span>
                ) : (
                  <span className="italic text-muted-foreground">ninguna</span>
                )}
              </div>
            </CardContent>
          </Card>
        );
      })}
    </div>
  );
}
