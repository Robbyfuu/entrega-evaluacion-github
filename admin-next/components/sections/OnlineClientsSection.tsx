"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { supabase } from "@/lib/supabase";
import type { OnlineClientRow, SuspiciousProcess } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { fmt, timeAgo } from "@/lib/format";
import { FALLBACK_SUSPICIOUS_PROCESSES, normalizeProcessName } from "@/lib/suspicious";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";

const ONLINE_WINDOW_MS = 90_000;

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

  const emptyMessage =
    rows.length > 0
      ? `Sin PCs conectados ahora (${rows.length} en total, ninguno con heartbeat reciente).`
      : "Sin PCs conectados.";

  return (
    <div className="card" id="sec-pcs">
      <h2>
        PCs conectados ahora
        <span style={{ fontSize: 12, color: "var(--text-faint)", marginLeft: 8 }}>
          (heartbeat &lt; 90s)
        </span>
        <span className="pill pill-live">{onlineData.length}</span>
      </h2>
      <p className="muted-note">Click en una fila para ver los programas abiertos en ese PC.</p>
      <table>
        <thead>
          <tr>
            <th>PC</th>
            <th>Usuario GitHub</th>
            <th>Sección</th>
            <th>Última señal</th>
            <th>Apps abiertas</th>
            <th>Internet</th>
            <th>Lockdown</th>
            <th>Acción</th>
          </tr>
        </thead>
        <tbody>
          {loading && rows.length === 0 ? (
            <tr>
              <td colSpan={8} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                Cargando...
              </td>
            </tr>
          ) : error ? (
            <tr>
              <td colSpan={8} className="err">
                Error: {error}
              </td>
            </tr>
          ) : onlineData.length === 0 ? (
            <tr>
              <td colSpan={8} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                {emptyMessage}
              </td>
            </tr>
          ) : (
            onlineData.map((c) => {
              const procs = Array.isArray(c.processes) ? c.processes : [];
              const clientSection = sectionCodeById(c.section_id) ?? c.section ?? null;
              const suspCount = procs.filter((p) =>
                isSuspiciousFor(p.name, clientSection)
              ).length;
              return (
                <tr
                  key={`${c.pc_name}|${c.github_username}|${c.evaluation_id ?? 0}`}
                  style={{ cursor: "pointer" }}
                  onClick={() => onOpenProcesses(c)}
                >
                  <td>{c.pc_name || "-"}</td>
                  <td>
                    {c.github_username ? (
                      <Badge solidColor={BADGE.user}>@{c.github_username}</Badge>
                    ) : (
                      "(sin login)"
                    )}
                  </td>
                  <td>
                    {clientSection ? (
                      <Badge solidColor={BADGE.sectionAlt}>{clientSection}</Badge>
                    ) : (
                      "-"
                    )}
                  </td>
                  <td>
                    {fmt(c.last_seen)} ({timeAgo(c.last_seen, now)})
                  </td>
                  <td>
                    {procs.length}
                    {suspCount > 0 ? (
                      <Badge solidColor={BADGE.danger} style={{ marginLeft: 8 }}>
                        ⚠ {suspCount} sosp.
                      </Badge>
                    ) : null}
                  </td>
                  <td>
                    <Badge
                      solidColor={
                        c.internet_state === "blocked" ? BADGE.danger : BADGE.success
                      }
                    >
                      {c.internet_state === "blocked" ? "BLOQUEADO" : "libre"}
                    </Badge>
                  </td>
                  <td>
                    <Badge
                      solidColor={
                        c.lockdown_state === "active" ? BADGE.lockdown : BADGE.neutral
                      }
                    >
                      {c.lockdown_state === "active" ? "ACTIVO" : "no"}
                    </Badge>
                  </td>
                  <td onClick={(e) => e.stopPropagation()}>
                    <button
                      className="btn-danger"
                      style={{ padding: "4px 10px", fontSize: 12, height: "auto" }}
                      onClick={(e) => {
                        e.stopPropagation();
                        void targetLockdown(c.pc_name, c.github_username);
                      }}
                    >
                      🔴 Lockdown
                    </button>
                    <button
                      className="btn-secondary"
                      title="Liberar lockdown dirigido"
                      style={{
                        padding: "4px 8px",
                        fontSize: 12,
                        height: "auto",
                        marginLeft: 6,
                      }}
                      onClick={(e) => {
                        e.stopPropagation();
                        void releaseTargetLockdown(c.pc_name, c.github_username);
                      }}
                    >
                      🔓
                    </button>
                  </td>
                </tr>
              );
            })
          )}
        </tbody>
      </table>
      <button className="btn-secondary" style={{ marginTop: 16 }} onClick={refresh}>
        Refrescar
      </button>
    </div>
  );
}
