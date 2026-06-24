"use client";

import { useEffect, useState } from "react";
import { AlertTriangle, ExternalLink, Globe, Lock, ShieldAlert, Unlock } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type {
  BrowserHistoryRow,
  CheatEventRow,
  ProcessAlertRow,
} from "@/lib/types";
import type { UnifiedStudent } from "@/lib/section-workspace";
import { fmt, timeAgo } from "@/lib/format";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/button";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { ScrollArea } from "@/components/ui/scroll-area";

interface StudentDrawerProps {
  student: UnifiedStudent | null;
  sectionCode: string | null;
  isSuspiciousFor: (name: string | null | undefined, sectionCode: string | null) => boolean;
  onClose: () => void;
  onTargetLockdown: (pc: string | null, github: string | null) => void;
  onReleaseLockdown: (pc: string | null, github: string | null) => void;
}

// Bloque titulado reutilizable dentro del drawer.
function Block({ title, count, children }: { title: string; count?: number; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">{title}</h3>
        {count !== undefined ? (
          <span className="text-xs text-muted-foreground tabular-nums">{count}</span>
        ) : null}
      </div>
      {children}
    </div>
  );
}

export function StudentDrawer({
  student,
  sectionCode,
  isSuspiciousFor,
  onClose,
  onTargetLockdown,
  onReleaseLockdown,
}: StudentDrawerProps) {
  const [alerts, setAlerts] = useState<ProcessAlertRow[]>([]);
  const [browsing, setBrowsing] = useState<BrowserHistoryRow[]>([]);
  const [cheats, setCheats] = useState<CheatEventRow[]>([]);
  const [submission, setSubmission] = useState<{ repo_url: string; submitted_at: string | null } | null>(null);

  const github = student?.github ?? null;
  const pc = student?.client?.pc_name ?? null;

  // Fetch puntual al abrir/cambiar de alumno (sin realtime: el detalle es a
  // demanda; los procesos vivos vienen del cliente ya suscripto en el padre).
  useEffect(() => {
    if (!github) {
      setAlerts([]);
      setBrowsing([]);
      setCheats([]);
      setSubmission(null);
      return;
    }
    let cancelled = false;
    (async () => {
      const [a, b, c, s] = await Promise.all([
        supabase
          .from("process_alerts")
          .select("*")
          .eq("github_username", github)
          .order("detected_at", { ascending: false })
          .limit(20),
        supabase
          .from("browser_history")
          .select("*")
          .eq("github_username", github)
          .order("visited_at", { ascending: false })
          .limit(20),
        supabase
          .from("cheat_events")
          .select("*")
          .eq("username", github)
          .order("detected_at", { ascending: false })
          .limit(10),
        supabase
          .from("assignment_submissions")
          .select("repo_url,submitted_at")
          .eq("github_username", github)
          .order("submitted_at", { ascending: false })
          .limit(1),
      ]);
      if (cancelled) return;
      setAlerts((a.data as ProcessAlertRow[]) ?? []);
      setBrowsing((b.data as BrowserHistoryRow[]) ?? []);
      setCheats((c.data as CheatEventRow[]) ?? []);
      const sub = (s.data as { repo_url: string; submitted_at: string | null }[] | null)?.[0] ?? null;
      setSubmission(sub);
    })();
    return () => {
      cancelled = true;
    };
  }, [github]);

  const procs = Array.isArray(student?.client?.processes) ? student!.client!.processes! : [];

  return (
    <Sheet open={!!student} onOpenChange={(o) => !o && onClose()}>
      <SheetContent side="right" className="w-[92vw] gap-0 p-0 sm:max-w-xl">
        {student ? (
          <>
            <SheetHeader className="border-b">
              <SheetTitle className="flex flex-wrap items-center gap-2">
                {student.fullName ?? "Alumno fuera de roster"}
                {student.github ? <Badge solidColor={BADGE.user}>@{student.github}</Badge> : null}
                <Badge solidColor={student.online ? BADGE.success : BADGE.neutral}>
                  {student.online ? "Online" : "Offline"}
                </Badge>
              </SheetTitle>
              <SheetDescription className="flex flex-wrap items-center gap-2">
                {student.version ? <span>v{student.version}</span> : <span>sin versión</span>}
                {student.client?.pc_name ? <span>· {student.client.pc_name}</span> : null}
                {student.client?.last_seen ? (
                  <span>· {timeAgo(student.client.last_seen)}</span>
                ) : null}
              </SheetDescription>
            </SheetHeader>

            <ScrollArea className="h-[calc(100vh-7rem)]">
              <div className="flex flex-col gap-6 p-4">
                {/* Estado */}
                <div className="flex flex-col gap-2">
                  <div className="flex flex-wrap gap-2">
                    <Badge solidColor={student.accepted ? BADGE.success : BADGE.neutral}>
                      {student.accepted ? "Aceptó ✓" : "No aceptó"}
                    </Badge>
                    <Badge solidColor={student.submitted ? BADGE.user : BADGE.neutral}>
                      {student.submitted ? "Entregó ✓" : "No entregó"}
                    </Badge>
                    {!student.githubResolved ? (
                      <Badge solidColor={BADGE.danger}>github sin asignar</Badge>
                    ) : null}
                  </div>
                  {/* Enlace de la entrega capturado al subir al repo */}
                  {/* Solo mostramos el enlace si entregó la evaluacion ACTUAL
                      (student.submitted ya viene scopeado a la tarea activa).
                      Asi no aparece un link de una entrega vieja. */}
                  {student.submitted && submission?.repo_url ? (
                    <>
                      <a
                        href={submission.repo_url}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="inline-flex items-center gap-1.5 text-sm text-primary underline-offset-2 hover:underline"
                      >
                        <ExternalLink className="size-3.5" />
                        <span className="truncate">{submission.repo_url}</span>
                      </a>
                      {submission.submitted_at ? (
                        <span className="text-xs text-muted-foreground">
                          Entregado: {fmt(submission.submitted_at)}
                        </span>
                      ) : null}
                    </>
                  ) : student.github ? (
                    <span className="text-xs text-muted-foreground">
                      Sin entrega de esta evaluación.
                    </span>
                  ) : null}
                </div>

                {/* Acciones */}
                <div className="flex flex-wrap gap-2">
                  <Button
                    variant="destructive"
                    size="sm"
                    onClick={() => onTargetLockdown(pc, github)}
                    disabled={!github}
                  >
                    <Lock className="size-3.5" /> Bloquear alumno
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => onReleaseLockdown(pc, github)}
                    disabled={!github}
                  >
                    <Unlock className="size-3.5" /> Liberar
                  </Button>
                </div>

                {/* Procesos en vivo */}
                <Block title="Procesos en vivo" count={procs.length}>
                  {!student.online ? (
                    <p className="text-sm text-muted-foreground">El alumno no está conectado.</p>
                  ) : procs.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Sin procesos reportados.</p>
                  ) : (
                    <div className="flex flex-col gap-1">
                      {procs.map((p, i) => {
                        const susp = isSuspiciousFor(p.name, sectionCode);
                        return (
                          <div
                            key={`${p.name}-${i}`}
                            className="flex items-center justify-between gap-2 rounded-md border px-2 py-1 text-sm"
                          >
                            <span className="truncate font-mono text-xs">{p.title || p.name}</span>
                            {susp ? (
                              <Badge solidColor={BADGE.danger}>
                                <ShieldAlert className="size-3" /> sosp.
                              </Badge>
                            ) : null}
                          </div>
                        );
                      })}
                    </div>
                  )}
                </Block>

                {/* Alertas */}
                <Block title="Alertas de procesos" count={alerts.length}>
                  {alerts.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Sin alertas.</p>
                  ) : (
                    <div className="flex flex-col gap-1">
                      {alerts.map((a, i) => (
                        <div key={a.id ?? i} className="flex items-start gap-2 rounded-md border px-2 py-1 text-sm">
                          <AlertTriangle className="mt-0.5 size-3.5 shrink-0 text-amber-500" />
                          <div className="min-w-0">
                            <div className="truncate font-mono text-xs">{a.process_name}</div>
                            <div className="text-xs text-muted-foreground">{fmt(a.detected_at)}</div>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </Block>

                {/* Navegación */}
                <Block title="Navegación reciente" count={browsing.length}>
                  {browsing.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Sin navegación registrada.</p>
                  ) : (
                    <div className="flex flex-col gap-1">
                      {browsing.map((b, i) => (
                        <div key={b.id ?? i} className="flex items-start gap-2 rounded-md border px-2 py-1 text-sm">
                          <Globe
                            className={cnGlobe(b.allowed)}
                          />
                          <div className="min-w-0">
                            <div className="truncate text-xs">{b.url}</div>
                            <div className="text-xs text-muted-foreground">
                              {b.allowed ? "permitida" : "BLOQUEADA"} · {fmt(b.visited_at)}
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </Block>

                {/* Trampas */}
                <Block title="Trampas" count={cheats.length}>
                  {cheats.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Sin eventos de trampa.</p>
                  ) : (
                    <div className="flex flex-col gap-1">
                      {cheats.map((c, i) => (
                        <div key={c.id ?? i} className="rounded-md border border-destructive/30 bg-destructive/5 px-2 py-1 text-sm">
                          <div className="text-xs">
                            {c.files_count ?? 0} archivo(s) no permitidos en {c.repo_name ?? "?"}
                          </div>
                          <div className="text-xs text-muted-foreground">{fmt(c.detected_at)}</div>
                        </div>
                      ))}
                    </div>
                  )}
                </Block>
              </div>
            </ScrollArea>
          </>
        ) : null}
      </SheetContent>
    </Sheet>
  );
}

function cnGlobe(allowed: boolean): string {
  return allowed
    ? "mt-0.5 size-3.5 shrink-0 text-muted-foreground"
    : "mt-0.5 size-3.5 shrink-0 text-destructive";
}
