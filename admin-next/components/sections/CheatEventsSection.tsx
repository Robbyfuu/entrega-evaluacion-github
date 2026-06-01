"use client";

import { useState } from "react";
import type { CheatEventRow } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { fmt } from "@/lib/format";
import { Badge } from "@/components/ui/Badge";
import { playAlertBeep } from "@/lib/sound";

// Eventos de trampa detectados (últimos 50). Live via cheat_events with a
// visual flash + sound cue when a new event arrives.
export function CheatEventsSection() {
  const [flashIds, setFlashIds] = useState<Set<string | number>>(new Set());

  const { rows, loading, error, refresh } = useRealtimeTable<
    CheatEventRow & Record<string, unknown>
  >({
    table: "cheat_events",
    order: { column: "detected_at", ascending: false },
    limit: 50,
    getId: (r) => r.id ?? `${r.detected_at}|${r.pc_name}|${r.username}`,
    onInsert: (row) => {
      const id = row.id ?? `${row.detected_at}|${row.pc_name}|${row.username}`;
      playAlertBeep();
      setFlashIds((prev) => new Set(prev).add(id));
      setTimeout(() => {
        setFlashIds((prev) => {
          const next = new Set(prev);
          next.delete(id);
          return next;
        });
      }, 1600);
    },
  });

  return (
    <div className="card" id="sec-cheat">
      <h2>
        Eventos de trampa detectados
        <span style={{ fontSize: 12, color: "var(--text-faint)", marginLeft: 8 }}>
          (últimos 50)
        </span>
      </h2>
      <table>
        <thead>
          <tr>
            <th>Fecha</th>
            <th>Usuario GitHub</th>
            <th>PC</th>
            <th>Repo</th>
            <th>Archivos</th>
          </tr>
        </thead>
        <tbody>
          {loading && rows.length === 0 ? (
            <tr>
              <td colSpan={5} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                Cargando...
              </td>
            </tr>
          ) : error ? (
            <tr>
              <td colSpan={5} className="err">
                Error: {error}
              </td>
            </tr>
          ) : rows.length === 0 ? (
            <tr>
              <td colSpan={5} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                Sin eventos de trampa registrados.
              </td>
            </tr>
          ) : (
            rows.map((e, i) => {
              const id = e.id ?? `${e.detected_at}|${e.pc_name}|${e.username}`;
              const sample = (e.files_sample ?? []).slice(0, 5).join(", ");
              return (
                <tr key={e.id ?? i} className={flashIds.has(id) ? "row-flash" : undefined}>
                  <td>{fmt(e.detected_at)}</td>
                  <td>
                    <Badge variant="cheat">{e.username || "(?)"}</Badge>
                  </td>
                  <td>{e.pc_name || "-"}</td>
                  <td>
                    <code>{e.repo_name || "-"}</code>
                  </td>
                  <td>
                    {e.files_count}: {sample}
                  </td>
                </tr>
              );
            })
          )}
        </tbody>
      </table>
      <button className="btn-secondary" style={{ marginTop: 16 }} onClick={refresh}>
        Refrescar eventos
      </button>
    </div>
  );
}
