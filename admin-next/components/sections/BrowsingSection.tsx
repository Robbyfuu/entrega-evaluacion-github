"use client";

import { useMemo, useState } from "react";
import type { BrowserHistoryRow } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { fmt } from "@/lib/format";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";

// Historial de navegación (últimos 100). Live via browser_history.
export function BrowsingSection() {
  const [onlyBlocked, setOnlyBlocked] = useState(false);
  const [flashIds, setFlashIds] = useState<Set<string | number>>(new Set());

  const { rows, loading, error, refresh } = useRealtimeTable<
    BrowserHistoryRow & Record<string, unknown>
  >({
    table: "browser_history",
    order: { column: "visited_at", ascending: false },
    limit: 100,
    getId: (r) => r.id ?? `${r.visited_at}|${r.url}`,
    onInsert: (row) => {
      if (row.allowed === false) {
        const id = row.id ?? `${row.visited_at}|${row.url}`;
        setFlashIds((prev) => new Set(prev).add(id));
        setTimeout(() => {
          setFlashIds((prev) => {
            const next = new Set(prev);
            next.delete(id);
            return next;
          });
        }, 1600);
      }
    },
  });

  const { sectionCodeById } = useSectionLookup();

  const visible = useMemo(
    () => (onlyBlocked ? rows.filter((r) => r.allowed === false) : rows),
    [rows, onlyBlocked]
  );

  const blockedCount = useMemo(
    () => rows.filter((r) => r.allowed === false).length,
    [rows]
  );

  return (
    <div className="card" id="sec-browsing">
      <h2>
        Historial de navegación
        <span style={{ fontSize: 12, color: "var(--text-faint)", marginLeft: 8 }}>
          (últimos 100)
        </span>
        <span className="pill pill-danger">{blockedCount} bloqueos</span>
      </h2>
      <p className="muted-note">
        Navegación del navegador interno del alumno. Solo se permiten GitHub, Microsoft
        (login) y Google (login/Gmail). Las filas rojas son intentos a sitios prohibidos
        (disparan trampa).
      </p>
      <div className="row-flex" style={{ marginBottom: 12 }}>
        <div className="field" style={{ flex: "0 0 200px" }}>
          <label htmlFor="browseFilter">Filtrar</label>
          <select
            id="browseFilter"
            value={onlyBlocked ? "blocked" : ""}
            onChange={(e) => setOnlyBlocked(e.target.value === "blocked")}
          >
            <option value="">Todo</option>
            <option value="blocked">Solo bloqueados</option>
          </select>
        </div>
        <button className="btn-secondary" onClick={refresh}>
          Refrescar
        </button>
      </div>
      <table>
        <thead>
          <tr>
            <th>Fecha</th>
            <th>Usuario</th>
            <th>PC</th>
            <th>Sección</th>
            <th>Estado</th>
            <th>URL</th>
          </tr>
        </thead>
        <tbody>
          {loading && rows.length === 0 ? (
            <tr>
              <td colSpan={6} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                Cargando...
              </td>
            </tr>
          ) : error ? (
            <tr>
              <td colSpan={6} className="err">
                Error: {error}
              </td>
            </tr>
          ) : visible.length === 0 ? (
            <tr>
              <td colSpan={6} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                Sin navegación registrada.
              </td>
            </tr>
          ) : (
            visible.map((r, i) => {
              const id = r.id ?? `${r.visited_at}|${r.url}`;
              const blocked = r.allowed === false;
              const cls =
                (blocked ? "row-blocked" : "") + (flashIds.has(id) ? " row-flash" : "");
              return (
                <tr key={r.id ?? i} className={cls.trim() || undefined}>
                  <td>{fmt(r.visited_at)}</td>
                  <td>
                    {r.github_username ? (
                      <Badge solidColor={BADGE.user}>@{r.github_username}</Badge>
                    ) : (
                      "-"
                    )}
                  </td>
                  <td>{r.pc_name || "-"}</td>
                  <td>{(sectionCodeById(r.section_id) ?? r.section) || "-"}</td>
                  <td>
                    <Badge variant={r.allowed ? "success" : "cheat"}>
                      {r.allowed ? "permitido" : "BLOQUEADO"}
                    </Badge>
                  </td>
                  <td
                    className="mono"
                    title={r.url || ""}
                    style={{
                      maxWidth: 360,
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                      fontSize: 12,
                    }}
                  >
                    {r.url || ""}
                  </td>
                </tr>
              );
            })
          )}
        </tbody>
      </table>
    </div>
  );
}
