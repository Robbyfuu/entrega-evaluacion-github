"use client";

import { useEffect, useMemo, useState } from "react";
import { CheckCircle2, Circle, ExternalLink } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type { EvaluationRow } from "@/lib/types";
import { fmt } from "@/lib/format";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { ScrollArea } from "@/components/ui/scroll-area";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Skeleton } from "@/components/ui/skeleton";

interface EnrollRow {
  id: number;
  full_name: string;
  github_username: string | null;
  status: string;
}
interface SubInfo {
  repo_url: string;
  submitted_at: string | null;
}

interface EvaluationStudentsDrawerProps {
  evaluation: EvaluationRow | null;
  sectionLabel: string;
  onClose: () => void;
}

// Alumnos del curso/seccion de una evaluacion, con el detalle de aceptó/entregó
// SCOPEADO a esa evaluacion (cruza por las tareas con evaluation_id = la eval).
// Se abre desde una fila de "Evaluaciones y tareas".
export function EvaluationStudentsDrawer({
  evaluation,
  sectionLabel,
  onClose,
}: EvaluationStudentsDrawerProps) {
  const [loading, setLoading] = useState(false);
  const [enrolls, setEnrolls] = useState<EnrollRow[]>([]);
  const [acceptedSet, setAcceptedSet] = useState<Set<string>>(new Set());
  const [subMap, setSubMap] = useState<Map<string, SubInfo>>(new Map());
  const [noTask, setNoTask] = useState(false);

  useEffect(() => {
    if (!evaluation) return;
    let cancelled = false;
    setLoading(true);
    (async () => {
      // 1) tareas (assignments) de esta evaluacion
      const { data: asg } = await supabase
        .from("assignments")
        .select("id")
        .eq("evaluation_id", evaluation.id);
      const aids = (asg ?? []).map((a) => a.id as number);

      // 2) roster de la seccion de la evaluacion
      const { data: roster } = await supabase
        .from("enrollments")
        .select("id,full_name,github_username,status")
        .eq("section_id", evaluation.section_id)
        .order("full_name");

      // 3) aceptaciones + entregas, scopeadas a las tareas de esta eval
      let accepted = new Set<string>();
      const sub = new Map<string, SubInfo>();
      if (aids.length > 0) {
        const [acc, subs] = await Promise.all([
          supabase.from("assignment_acceptances").select("github_username").in("assignment_id", aids),
          supabase
            .from("assignment_submissions")
            .select("github_username,repo_url,submitted_at")
            .in("assignment_id", aids)
            .order("submitted_at", { ascending: false }),
        ]);
        accepted = new Set(
          (acc.data ?? [])
            .map((r) => (r.github_username as string | null)?.toLowerCase())
            .filter((g): g is string => !!g)
        );
        for (const r of subs.data ?? []) {
          const gh = (r.github_username as string | null)?.toLowerCase();
          if (gh && !sub.has(gh)) sub.set(gh, { repo_url: r.repo_url, submitted_at: r.submitted_at });
        }
      }

      if (cancelled) return;
      setEnrolls((roster as EnrollRow[]) ?? []);
      setAcceptedSet(accepted);
      setSubMap(sub);
      setNoTask(aids.length === 0);
      setLoading(false);
    })();
    return () => {
      cancelled = true;
    };
  }, [evaluation]);

  const enrolled = useMemo(() => enrolls.filter((e) => e.status === "enrolled"), [enrolls]);
  const total = enrolled.length;
  const submittedCount = enrolled.filter(
    (e) => e.github_username && subMap.has(e.github_username.toLowerCase())
  ).length;
  const acceptedCount = enrolled.filter(
    (e) => e.github_username && acceptedSet.has(e.github_username.toLowerCase())
  ).length;

  return (
    <Sheet open={!!evaluation} onOpenChange={(o) => !o && onClose()}>
      <SheetContent side="right" className="w-[94vw] gap-0 p-0 sm:max-w-2xl">
        {evaluation ? (
          <>
            <SheetHeader className="border-b">
              <SheetTitle className="flex flex-wrap items-center gap-2">
                {evaluation.title}
                <Badge solidColor={BADGE.sectionAlt}>{sectionLabel}</Badge>
              </SheetTitle>
              <SheetDescription>
                {noTask ? (
                  <span className="text-amber-600 dark:text-amber-400">
                    Esta evaluación no tiene tarea/link de Classroom todavía.
                  </span>
                ) : (
                  <>
                    {total} alumnos · <strong className="text-foreground">{submittedCount}</strong> entregaron ·{" "}
                    {acceptedCount} aceptaron
                  </>
                )}
              </SheetDescription>
            </SheetHeader>

            <ScrollArea className="h-[calc(100vh-7rem)]">
              <div className="p-4">
                <div className="overflow-hidden rounded-lg border">
                  <Table>
                    <TableHeader className="bg-muted/50">
                      <TableRow>
                        {["Alumno", "GitHub", "Aceptó", "Entregó", "Entrega"].map((h) => (
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
                      {loading ? (
                        Array.from({ length: 6 }).map((_, i) => (
                          <TableRow key={`sk-${i}`}>
                            {Array.from({ length: 5 }).map((__, j) => (
                              <TableCell key={j}><Skeleton className="h-4 w-20" /></TableCell>
                            ))}
                          </TableRow>
                        ))
                      ) : enrolled.length === 0 ? (
                        <TableRow>
                          <TableCell colSpan={5} className="py-10 text-center text-sm text-muted-foreground">
                            Sin alumnos en el roster de esta sección.
                          </TableCell>
                        </TableRow>
                      ) : (
                        enrolled.map((e) => {
                          const gh = e.github_username?.toLowerCase() ?? null;
                          const accepted = gh ? acceptedSet.has(gh) : false;
                          const sub = gh ? subMap.get(gh) ?? null : null;
                          return (
                            <TableRow key={e.id}>
                              <TableCell className="font-medium">{e.full_name}</TableCell>
                              <TableCell>
                                {e.github_username ? (
                                  <Badge solidColor={BADGE.user}>@{e.github_username}</Badge>
                                ) : (
                                  <span className="text-xs text-muted-foreground">(sin github)</span>
                                )}
                              </TableCell>
                              <TableCell>
                                {accepted ? (
                                  <span className="inline-flex items-center gap-1 text-xs font-medium text-emerald-600 dark:text-emerald-400">
                                    <CheckCircle2 className="size-3.5" /> Sí
                                  </span>
                                ) : (
                                  <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
                                    <Circle className="size-3" /> No
                                  </span>
                                )}
                              </TableCell>
                              <TableCell>
                                {sub ? (
                                  <span className="inline-flex items-center gap-1 text-xs font-medium" style={{ color: BADGE.user }}>
                                    <CheckCircle2 className="size-3.5" /> Sí
                                  </span>
                                ) : (
                                  <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
                                    <Circle className="size-3" /> No
                                  </span>
                                )}
                              </TableCell>
                              <TableCell>
                                {sub?.repo_url ? (
                                  <a
                                    href={sub.repo_url}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="inline-flex max-w-[220px] items-center gap-1 truncate font-mono text-xs text-primary hover:underline"
                                  >
                                    <ExternalLink className="size-3 shrink-0" />
                                    <span className="truncate">{sub.repo_url}</span>
                                  </a>
                                ) : (
                                  <span className="text-xs text-muted-foreground">—</span>
                                )}
                              </TableCell>
                            </TableRow>
                          );
                        })
                      )}
                    </TableBody>
                  </Table>
                </div>
              </div>
            </ScrollArea>
          </>
        ) : null}
      </SheetContent>
    </Sheet>
  );
}
