"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { supabase } from "@/lib/supabase";
import type { StudentActivityRow } from "@/lib/types";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { fmt } from "@/lib/format";
import { ACTION_LABEL, ACTION_COLOR, BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";

// Actividad de alumnos (últimos 100), filtered by action and section.
// Uses manual fetch on filter change / Refrescar (realtime optional here).
export function ActivitySection() {
  const { sections, sectionCodeById } = useSectionLookup();
  const [actionFilter, setActionFilter] = useState("");
  const [sectionFilter, setSectionFilter] = useState("");
  const [rows, setRows] = useState<StudentActivityRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const sectionCodes = useMemo(() => {
    const codes = new Set<string>();
    for (const s of sections) codes.add(s.code);
    for (const r of rows) if (r.section) codes.add(r.section);
    return Array.from(codes).sort();
  }, [sections, rows]);

  const load = useCallback(async () => {
    setLoading(true);
    let q = supabase
      .from("student_activity")
      .select("*")
      .order("created_at", { ascending: false })
      .limit(100);
    if (actionFilter) q = q.eq("action", actionFilter);
    if (sectionFilter) q = q.eq("section", sectionFilter);
    const { data, error: err } = await q;
    if (err) {
      setError(err.message);
      setRows([]);
    } else {
      setError(null);
      setRows((data ?? []) as StudentActivityRow[]);
    }
    setLoading(false);
  }, [actionFilter, sectionFilter]);

  useEffect(() => {
    void load();
  }, [load]);

  const sectionLabel = (r: StudentActivityRow) =>
    sectionCodeById(r.section_id) ?? r.section ?? null;

  return (
    <div className="card" id="sec-activity">
      <h2>
        Actividad de alumnos
        <span style={{ fontSize: 12, color: "var(--text-faint)", marginLeft: 8 }}>
          (últimos 100)
        </span>
      </h2>
      <div className="row-flex" style={{ marginBottom: 12 }}>
        <div className="field" style={{ flex: "0 0 180px" }}>
          <label htmlFor="actionFilter">Filtrar por acción</label>
          <select
            id="actionFilter"
            value={actionFilter}
            onChange={(e) => setActionFilter(e.target.value)}
          >
            <option value="">Todas</option>
            <option value="login">Login</option>
            <option value="create_repo">Crear repo</option>
            <option value="clone">Clonar repo</option>
            <option value="upload">Subir archivos</option>
          </select>
        </div>
        <div className="field" style={{ flex: "0 0 160px" }}>
          <label htmlFor="sectionFilter">Filtrar por sección</label>
          <select
            id="sectionFilter"
            value={sectionFilter}
            onChange={(e) => setSectionFilter(e.target.value)}
          >
            <option value="">Todas</option>
            {sectionCodes.map((sec) => (
              <option key={sec} value={sec}>
                {sec}
              </option>
            ))}
          </select>
        </div>
        <button className="btn-secondary" onClick={load}>
          Refrescar
        </button>
      </div>
      <table>
        <thead>
          <tr>
            <th>Fecha</th>
            <th>Sección</th>
            <th>Usuario GitHub</th>
            <th>Email</th>
            <th>PC</th>
            <th>Acción</th>
            <th>Repo</th>
          </tr>
        </thead>
        <tbody>
          {loading && rows.length === 0 ? (
            <tr>
              <td colSpan={7} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                Cargando...
              </td>
            </tr>
          ) : error ? (
            <tr>
              <td colSpan={7} className="err">
                Error: {error}
              </td>
            </tr>
          ) : rows.length === 0 ? (
            <tr>
              <td colSpan={7} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                Sin actividad registrada.
              </td>
            </tr>
          ) : (
            rows.map((e, i) => {
              const sec = sectionLabel(e);
              return (
                <tr key={e.id ?? i}>
                  <td>{fmt(e.created_at)}</td>
                  <td>
                    {sec ? <Badge solidColor={BADGE.user}>{sec}</Badge> : "-"}
                  </td>
                  <td>@{e.github_username || "?"}</td>
                  <td>{e.github_email || "-"}</td>
                  <td>{e.pc_name || "-"}</td>
                  <td>
                    <Badge solidColor={ACTION_COLOR[e.action] || "#999"}>
                      {ACTION_LABEL[e.action] || e.action}
                    </Badge>
                  </td>
                  <td>
                    {e.repo_url ? (
                      <a href={e.repo_url} target="_blank" rel="noopener noreferrer">
                        {e.repo_name || e.repo_url}
                      </a>
                    ) : (
                      e.repo_name || "-"
                    )}
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
