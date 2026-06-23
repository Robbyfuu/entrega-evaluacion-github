"use client";

import { ChevronRight, ShieldAlert, Users, Wifi } from "lucide-react";
import type { SectionRow } from "@/lib/types";
import type { SectionStats } from "@/lib/section-workspace";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/Badge";
import { BADGE } from "@/lib/colors";
import { Skeleton } from "@/components/ui/skeleton";

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
            className="cursor-pointer transition-colors hover:border-primary/50 hover:bg-accent/30"
            onClick={() => onSelectSection(sec.id)}
          >
            <CardContent className="flex flex-col gap-4 p-5">
              <div className="flex items-start justify-between">
                <div>
                  <div className="text-lg font-bold">{sec.code}</div>
                  {course ? (
                    <div className="text-xs text-muted-foreground">{course}</div>
                  ) : null}
                </div>
                <ChevronRight className="size-5 text-muted-foreground" />
              </div>

              <div className="flex flex-wrap items-center gap-2 text-sm">
                <span className="inline-flex items-center gap-1">
                  <Wifi className="size-4 text-emerald-500" />
                  <span className="font-semibold tabular-nums">{st.online}</span>
                  <span className="text-muted-foreground">online</span>
                </span>
                <span className="inline-flex items-center gap-1">
                  <Users className="size-4 text-muted-foreground" />
                  <span className="font-semibold tabular-nums">
                    {st.enrolled ?? "—"}
                  </span>
                  <span className="text-muted-foreground">roster</span>
                </span>
                {st.suspicious > 0 ? (
                  <Badge solidColor={BADGE.danger}>
                    <ShieldAlert className="size-3" /> {st.suspicious}
                  </Badge>
                ) : null}
              </div>

              <div className="flex items-center gap-3 text-xs text-muted-foreground">
                <span>Aceptaron <strong className="text-foreground tabular-nums">{st.accepted}</strong></span>
                <span>Entregaron <strong className="text-foreground tabular-nums">{st.submitted}</strong></span>
              </div>

              <div className="border-t pt-3 text-xs">
                <span className="text-muted-foreground">Evaluación activa: </span>
                {evalTitle ? (
                  <span className="font-medium text-foreground">{evalTitle}</span>
                ) : (
                  <span className="text-muted-foreground italic">ninguna</span>
                )}
              </div>
            </CardContent>
          </Card>
        );
      })}
    </div>
  );
}
