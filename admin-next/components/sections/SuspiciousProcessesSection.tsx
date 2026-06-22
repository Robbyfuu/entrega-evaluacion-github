"use client";

import { useMemo, useState } from "react";
import { supabase } from "@/lib/supabase";
import type { SuspiciousProcess } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { normalizeProcessName } from "@/lib/suspicious";
import { fmt } from "@/lib/format";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";

const GLOBAL_LABEL = "Global (todas las secciones)";

// Procesos sospechosos: editor CRUD sobre la tabla `suspicious_processes`.
// El cliente C# y el panel comparten esta tabla en tiempo real, así el profesor
// edita la lista y los equipos la aplican sin reiniciar.
export function SuspiciousProcessesSection() {
  const { rows, loading, error, refresh } = useRealtimeTable<
    SuspiciousProcess & Record<string, unknown>
  >({
    table: "suspicious_processes",
    order: { column: "process_name", ascending: true },
    getId: (r) => r.id,
  });

  const { sections } = useSectionLookup();

  const [feedback, setFeedback] = useState<{ text: string; ok: boolean } | null>(null);
  const [name, setName] = useState("");
  const [section, setSection] = useState<string>("");

  // Section codes come from the DB (dynamic) plus any extra value seen in
  // existing rows, so the editor never hides a row even if its section was
  // deleted from the sections table.
  const sectionCodes = useMemo(() => {
    const codes = new Set<string>();
    for (const s of sections) codes.add(s.code);
    for (const r of rows) if (r.section) codes.add(r.section);
    return Array.from(codes).sort();
  }, [sections, rows]);

  // Group rows: a "Global" bucket (section === null) first, then one bucket per
  // known section.
  const groups = useMemo(() => {
    const global = rows.filter((r) => r.section === null);
    const bySection = sectionCodes.map((sec) => ({
      key: sec,
      label: sec,
      items: rows.filter((r) => r.section === sec),
    }));

    return [{ key: "__global__", label: GLOBAL_LABEL, items: global }, ...bySection];
  }, [rows, sectionCodes]);

  async function addProcess() {
    const normalized = normalizeProcessName(name);
    if (!normalized) {
      setFeedback({ text: "Escribe el nombre del proceso.", ok: false });
      return;
    }
    // .select() para confirmar que la fila se escribio: si la sesion expiro,
    // RLS rechaza el write sin lanzar error (data vacia, error null). Sin esta
    // verificacion el profe veria "agregado" mientras NADA se escribio: falso
    // negativo de proctoring. data vacia => tratamos como error de permisos.
    const { data, error: err } = await supabase
      .from("suspicious_processes")
      .insert({
        process_name: normalized,
        section: section === "" ? null : section,
      })
      .select();
    if (err) {
      // 23505 = unique_violation. Surface a friendly message for duplicates.
      const duplicate = err.code === "23505" || /duplicate|unique/i.test(err.message);
      setFeedback({
        text: duplicate
          ? `"${normalized}" ya está en la lista para esa sección.`
          : "Error: " + err.message,
        ok: false,
      });
      return;
    }
    if (!data || data.length === 0) {
      setFeedback({
        text: "No se pudo agregar (¿sesión expirada?). Vuelve a iniciar sesión.",
        ok: false,
      });
      return;
    }
    setName("");
    setFeedback({ text: `Proceso "${normalized}" agregado.`, ok: true });
    void refresh();
  }

  async function deleteProcess(id: SuspiciousProcess["id"]) {
    if (!window.confirm("¿Eliminar este proceso de la lista?")) return;
    // .select() para confirmar el borrado: misma razon que en addProcess, si
    // RLS rechaza por sesion expirada no se lanza error pero no se borra nada.
    const { data, error: err } = await supabase
      .from("suspicious_processes")
      .delete()
      .eq("id", id)
      .select();
    if (err) {
      setFeedback({ text: "Error: " + err.message, ok: false });
      return;
    }
    if (!data || data.length === 0) {
      setFeedback({
        text: "No se pudo eliminar (¿sesión expirada?). Vuelve a iniciar sesión.",
        ok: false,
      });
      return;
    }
    void refresh();
  }

  const total = rows.length;

  return (
    <div className="card" id="sec-suspicious">
      <h2>
        Procesos sospechosos
        <span style={{ fontSize: 12, color: "var(--text-faint)", marginLeft: 8 }}>
          (resaltan en PCs conectados y alertas)
        </span>
        <span className="pill">{total}</span>
      </h2>
      <p className="muted-note">
        Lista de programas que se marcan como sospechosos durante el examen. Usa{" "}
        <strong>Global</strong> para todas las secciones, o limita un proceso a una
        sección. Los equipos la aplican en tiempo real.
      </p>
      <div className="row-flex">
        <div className="field">
          <label htmlFor="suspName">Nombre del proceso</label>
          <input
            type="text"
            id="suspName"
            placeholder="Nombre del proceso (ej: chrome, code, discord)"
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") void addProcess();
            }}
          />
        </div>
        <div className="field" style={{ flex: "0 0 220px" }}>
          <label htmlFor="suspSection">Sección</label>
          <select
            id="suspSection"
            value={section}
            onChange={(e) => setSection(e.target.value)}
          >
            <option value="">{GLOBAL_LABEL}</option>
            {sectionCodes.map((sec) => (
              <option key={sec} value={sec}>
                {sec}
              </option>
            ))}
          </select>
        </div>
        <button className="btn-primary" onClick={addProcess}>
          Agregar proceso
        </button>
      </div>
      {feedback ? <div className={feedback.ok ? "ok" : "err"}>{feedback.text}</div> : null}

      {loading && rows.length === 0 ? (
        <p style={{ marginTop: 16, color: "var(--text-faint)" }}>Cargando...</p>
      ) : error ? (
        <p className="err" style={{ marginTop: 16 }}>
          Error: {error}
        </p>
      ) : (
        <div style={{ marginTop: 16 }}>
          {groups.map((group) => (
            <div key={group.key} style={{ marginBottom: 20 }}>
              <h3 style={{ fontSize: 14, margin: "0 0 8px" }}>
                <Badge
                  solidColor={group.key === "__global__" ? BADGE.sectionAlt : BADGE.user}
                >
                  {group.label}
                </Badge>
                <span
                  style={{ fontSize: 12, color: "var(--text-faint)", marginLeft: 8 }}
                >
                  {group.items.length}
                </span>
              </h3>
              {group.items.length === 0 ? (
                <p style={{ color: "var(--text-faint)", fontSize: 13, margin: 0 }}>
                  Sin procesos en este grupo.
                </p>
              ) : (
                <table>
                  <thead>
                    <tr>
                      <th style={{ width: "50%" }}>Nombre del proceso</th>
                      <th style={{ width: "35%" }}>Agregado</th>
                      <th style={{ width: "15%" }}>Acciones</th>
                    </tr>
                  </thead>
                  <tbody>
                    {group.items.map((p) => (
                      <tr key={p.id}>
                        <td className="mono">{p.process_name}</td>
                        <td style={{ fontSize: 12, color: "var(--text-faint)" }}>
                          {fmt(p.created_at)}
                        </td>
                        <td>
                          <button
                            className="btn-danger"
                            style={{ padding: "4px 12px", fontSize: 12, height: "auto" }}
                            onClick={() => deleteProcess(p.id)}
                          >
                            Eliminar
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          ))}
        </div>
      )}
      <button className="btn-secondary" style={{ marginTop: 8 }} onClick={refresh}>
        Refrescar
      </button>
    </div>
  );
}
