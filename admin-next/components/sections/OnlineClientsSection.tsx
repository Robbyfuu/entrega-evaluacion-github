"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Lock, MonitorOff, MonitorSmartphone, RefreshCw, Unlock } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type { OnlineClientRow, SuspiciousProcess } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { fmt, timeAgo } from "@/lib/format";
import { FALLBACK_SUSPICIOUS_PROCESSES, normalizeProcessName } from "@/lib/suspicious";
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

const ONLINE_WINDOW_MS = 90_000;

// Compara versiones "x.y.z": >0 si a>b, <0 si a<b, 0 iguales.
function cmpVer(a: string, b: string): number {
  const pa = a.split(".").map((n) => parseInt(n, 10) || 0);
  const pb = b.split(".").map((n) => parseInt(n, 10) || 0);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (d !== 0) return d;
  }
  return 0;
}

interface OnlineClientsSectionProps {
  onOpenProcesses: (client: OnlineClientRow) => void;
  onOnlineCountChange: (count: number) => void;
}

// PCs conectados (heartbeat < 90s). Live via realtime on online_clients.
export function OnlineClientsSection({
  onOpenProcesses,
  onOnlineCountChange,
}: OnlineClientsSectionProps) {
  const { rows, loading, error, refresh } = useRealtimeTable<
    OnlineClientRow & Record<string, unknown>
  >({
    table: "online_clients",
    order: { column: "last_seen", ascending: false },
    limit: 100,
    // Identity mirrors the (pc_name, github_username, COALESCE(evaluation_id,0))
    // re-sit isolation key: the same PC re-sitting another evaluation is a
    // distinct live row, not an overwrite.
    getId: (r) => `${r.pc_name}|${r.github_username}|${r.evaluation_id ?? 0}`,
  });

  const { sectionCodeById } = useSectionLookup();

  // Live blocklist. A process is suspicious for a client when its normalized
  // name is global (section === null) or scoped to the client's own section.
  const { rows: suspiciousRows } = useRealtimeTable<
    SuspiciousProcess & Record<string, unknown>
  >({
    table: "suspicious_processes",
    order: { column: "process_name", ascending: true },
    getId: (r) => r.id,
  });

  // Build per-section lookup. Falls back to the static set when the table is
  // empty/unavailable, mirroring the client's fallback invariant.
  const suspicious = useMemo(() => {
    const globalSet = new Set<string>();
    const bySection = new Map<string, Set<string>>();
    for (const r of suspiciousRows) {
      const norm = normalizeProcessName(r.process_name);
      if (!norm) continue;
      if (r.section === null) {
        globalSet.add(norm);
      } else {
        const set = bySection.get(r.section) ?? new Set<string>();
        set.add(norm);
        bySection.set(r.section, set);
      }
    }
    const useFallback = suspiciousRows.length === 0;
    return { globalSet, bySection, useFallback };
  }, [suspiciousRows]);

  const isSuspiciousFor = useCallback(
    (procName: string | null | undefined, clientSection: string | null) => {
      const norm = normalizeProcessName(procName ?? "");
      if (!norm) return false;
      if (suspicious.useFallback) return FALLBACK_SUSPICIOUS_PROCESSES.has(norm);
      if (suspicious.globalSet.has(norm)) return true;
      if (clientSection) return suspicious.bySection.get(clientSection)?.has(norm) ?? false;
      return false;
    },
    [suspicious]
  );

  // Tick every 5s so the "online" filter and "hace Ns" labels stay fresh.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 5000);
    return () => clearInterval(id);
  }, []);

  const onlineData = useMemo(() => {
    const cutoff = now - ONLINE_WINDOW_MS;
    return rows.filter((c) => new Date(c.last_seen).getTime() >= cutoff);
  }, [rows, now]);

  useEffect(() => {
    onOnlineCountChange(onlineData.length);
  }, [onlineData.length, onOnlineCountChange]);

  async function targetLockdown(pcName: string | null, githubUsername: string | null) {
    if (!githubUsername) {
      window.alert("Este PC no tiene usuario GitHub registrado.");
      return;
    }
    const reason = window.prompt(
      `Lockdown DIRIGIDO a ${pcName} (@${githubUsername}).\nMotivo (opcional):`,
      "Trampa detectada por el profesor"
    );
    if (reason === null) return;
    const { error: err } = await supabase.from("targeted_lockdowns").upsert(
      {
        pc_name: pcName,
        github_username: githubUsername,
        active: true,
        reason: reason || "Bloqueo del profesor",
        released_at: null,
      },
      { onConflict: "pc_name,github_username" }
    );
    window.alert(
      err ? "Error: " + err.message : `✓ Lockdown enviado a ${pcName}. Se aplicará en <20s.`
    );
  }

  async function releaseTargetLockdown(
    pcName: string | null,
    githubUsername: string | null
  ) {
    const { error: err } = await supabase
      .from("targeted_lockdowns")
      .update({ active: false, released_at: new Date().toISOString() })
      .match({ pc_name: pcName, github_username: githubUsername });
    window.alert(
      err ? "Error: " + err.message : `✓ Lockdown liberado para ${pcName}.`
    );
  }

  // Pide a TODOS los clientes que actualicen: setea control.update_requested_at
  // = NOW(). Los clientes que ya estaban abiertos lo ven en su AdminTick y
  // disparan el update (manual; no hay fetch automatico). Update real solo si
  // hay version nueva publicada.
  async function requestUpdateForAll() {
    if (
      !window.confirm(
        "Pedir a TODOS los PCs conectados que busquen actualizacion?\n" +
          "Los que tengan version nueva se reiniciaran para aplicarla."
      )
    )
      return;
    const { error: err } = await supabase
      .from("control")
      .update({ update_requested_at: new Date().toISOString() })
      .eq("id", 1);
    window.alert(
      err
        ? "Error: " + err.message
        : "✓ Solicitud enviada. Los PCs abiertos buscaran actualizacion en <20s."
    );
  }

  // Version mas alta vista entre los conectados = referencia para marcar
  // desactualizados (no hardcodeamos la ultima version).
  const latestVersion = onlineData.reduce<string | null>((max, c) => {
    const v = c.app_version;
    if (!v) return max;
    return !max || cmpVer(v, max) > 0 ? v : max;
  }, null);

  const emptyMessage =
    rows.length > 0
      ? `Sin PCs conectados ahora (${rows.length} en total, ninguno con heartbeat reciente).`
      : "Sin PCs conectados.";

  return (
    <Card id="sec-pcs" className="scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <MonitorSmartphone className="size-5 text-primary" />
          PCs conectados ahora
          <span className="text-xs font-normal text-muted-foreground">
            (heartbeat &lt; 90s)
          </span>
          <span className="ml-auto inline-flex h-6 min-w-6 items-center justify-center rounded-full bg-emerald-500/10 px-2 text-sm font-semibold text-emerald-500 tabular-nums">
            {onlineData.length}
          </span>
          <Button variant="outline" size="sm" onClick={requestUpdateForAll}>
            <RefreshCw className="size-3.5" />
            Solicitar actualización
          </Button>
        </CardTitle>
        <CardDescription>
          Click en una fila para ver los programas abiertos en ese PC.
          {latestVersion ? (
            <> Última versión vista: <strong className="font-semibold text-foreground">v{latestVersion}</strong>.</>
          ) : null}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">PC</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Usuario GitHub</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Sección</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Última señal</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Apps abiertas</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Internet</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Lockdown</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Versión</TableHead>
              <TableHead className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Acción</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading && rows.length === 0 ? (
              Array.from({ length: 4 }).map((_, i) => (
                <TableRow key={`sk-${i}`}>
                  <TableCell><Skeleton className="h-4 w-24" /></TableCell>
                  <TableCell><Skeleton className="h-5 w-28 rounded-full" /></TableCell>
                  <TableCell><Skeleton className="h-5 w-16 rounded-full" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-32" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-10" /></TableCell>
                  <TableCell><Skeleton className="h-5 w-20 rounded-full" /></TableCell>
                  <TableCell><Skeleton className="h-5 w-16 rounded-full" /></TableCell>
                  <TableCell><Skeleton className="h-8 w-28" /></TableCell>
                </TableRow>
              ))
            ) : error ? (
              <TableRow>
                <TableCell colSpan={9} className="py-8 text-center text-destructive">
                  Error: {error}
                </TableCell>
              </TableRow>
            ) : onlineData.length === 0 ? (
              <TableRow>
                <TableCell colSpan={9} className="py-10">
                  <div className="flex flex-col items-center gap-2 text-center text-muted-foreground">
                    <MonitorOff className="size-8 text-muted-foreground/40" />
                    <p className="text-sm">{emptyMessage}</p>
                  </div>
                </TableCell>
              </TableRow>
            ) : (
              onlineData.map((c) => {
                const procs = Array.isArray(c.processes) ? c.processes : [];
                const clientSection = sectionCodeById(c.section_id) ?? c.section ?? null;
                const suspCount = procs.filter((p) =>
                  isSuspiciousFor(p.name, clientSection)
                ).length;
                return (
                  <TableRow
                    key={`${c.pc_name}|${c.github_username}|${c.evaluation_id ?? 0}`}
                    className="cursor-pointer"
                    onClick={() => onOpenProcesses(c)}
                  >
                    <TableCell className="font-medium">{c.pc_name || "-"}</TableCell>
                    <TableCell>
                      {c.github_username ? (
                        <Badge solidColor={BADGE.user}>@{c.github_username}</Badge>
                      ) : (
                        <span className="text-muted-foreground">(sin login)</span>
                      )}
                    </TableCell>
                    <TableCell>
                      {clientSection ? (
                        <Badge solidColor={BADGE.sectionAlt}>{clientSection}</Badge>
                      ) : (
                        "-"
                      )}
                    </TableCell>
                    <TableCell className="text-muted-foreground tabular-nums">
                      {fmt(c.last_seen)} ({timeAgo(c.last_seen, now)})
                    </TableCell>
                    <TableCell>
                      <span className="font-medium tabular-nums">{procs.length}</span>
                      {suspCount > 0 ? (
                        <Badge solidColor={BADGE.danger} style={{ marginLeft: 8 }}>
                          ⚠ {suspCount} sosp.
                        </Badge>
                      ) : null}
                    </TableCell>
                    <TableCell>
                      <Badge
                        solidColor={
                          c.internet_state === "blocked" ? BADGE.danger : BADGE.success
                        }
                      >
                        {c.internet_state === "blocked" ? "BLOQUEADO" : "libre"}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <Badge
                        solidColor={
                          c.lockdown_state === "active" ? BADGE.lockdown : BADGE.neutral
                        }
                      >
                        {c.lockdown_state === "active" ? "ACTIVO" : "no"}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      {c.app_version ? (
                        <Badge
                          solidColor={
                            latestVersion && cmpVer(c.app_version, latestVersion) < 0
                              ? BADGE.danger
                              : BADGE.neutral
                          }
                        >
                          v{c.app_version}
                          {latestVersion && cmpVer(c.app_version, latestVersion) < 0
                            ? " ⚠"
                            : ""}
                        </Badge>
                      ) : (
                        <span className="text-muted-foreground">—</span>
                      )}
                    </TableCell>
                    <TableCell onClick={(e) => e.stopPropagation()}>
                      <div className="flex items-center gap-1.5">
                        <Button
                          variant="destructive"
                          size="sm"
                          onClick={(e) => {
                            e.stopPropagation();
                            void targetLockdown(c.pc_name, c.github_username);
                          }}
                        >
                          <Lock className="size-3.5" />
                          Lockdown
                        </Button>
                        <Button
                          variant="outline"
                          size="icon-sm"
                          title="Liberar lockdown dirigido"
                          onClick={(e) => {
                            e.stopPropagation();
                            void releaseTargetLockdown(c.pc_name, c.github_username);
                          }}
                        >
                          <Unlock className="size-3.5" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })
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
