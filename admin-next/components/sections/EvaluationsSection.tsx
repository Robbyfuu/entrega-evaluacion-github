"use client";

import { useMemo, useState } from "react";
import { supabase } from "@/lib/supabase";
import type { EvaluationRow, SectionRow } from "@/lib/types";
import { useEvaluations } from "@/hooks/useEvaluations";
import { useSections } from "@/hooks/useSections";
import { useCourses } from "@/hooks/useCourses";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";

export function EvaluationsSection() {
  const { rows, loading, error, refresh } = useEvaluations();
  const { rows: sections } = useSections();
  const { rows: courses } = useCourses();
  const [feedback, setFeedback] = useState<{ text: string; ok: boolean } | null>(null);
  const [sectionId, setSectionId] = useState<string>("");
  const [title, setTitle] = useState("");
  const [url, setUrl] = useState("");
  const [org, setOrg] = useState("");

  const sectionMap = useMemo(() => {
    const m = new Map<number, SectionRow>();
    for (const s of sections) m.set(s.id, s);
    return m;
  }, [sections]);

  const courseMap = useMemo(() => {
    const m = new Map<number, string>();
    for (const c of courses) m.set(c.id, c.code);
    return m;
  }, [courses]);

  async function addEvaluation() {
    const sid = Number(sectionId);
    const t = title.trim();
    if (!sid || !t) {
      setFeedback({ text: "Selecciona sección y completa el título.", ok: false });
      return;
    }
    const { data, error: err } = await supabase
      .from("evaluations")
      .insert({
        section_id: sid,
        title: t,
        classroom_url: url.trim() || null,
        org: org.trim() || null,
        active: false,
      })
      .select();
    if (err) {
      setFeedback({ text: "Error: " + err.message, ok: false });
      return;
    }
    if (!data || data.length === 0) {
      setFeedback({ text: "No se pudo agregar (¿sesión expirada?).", ok: false });
      return;
    }
    setTitle("");
    setUrl("");
    setOrg("");
    setFeedback({ text: `Evaluación "${t}" agregada (inactiva).`, ok: true });
    void refresh();
  }

  async function toggleEvaluation(id: EvaluationRow["id"], active: boolean) {
    const { data, error: err } = await supabase.from("evaluations").update({ active }).eq("id", id).select();
    if (err) {
      setFeedback({ text: "Error: " + err.message, ok: false });
      return;
    }
    if (!data || data.length === 0) {
      setFeedback({ text: "No se pudo actualizar (¿sesión expirada?).", ok: false });
      return;
    }
    void refresh();
  }

  async function deleteEvaluation(id: EvaluationRow["id"], title: string) {
    if (!window.confirm(`¿Eliminar la evaluación "${title}"?`)) return;
    const { data, error: err } = await supabase.from("evaluations").delete().eq("id", id).select();
    if (err) {
      setFeedback({ text: "Error: " + err.message, ok: false });
      return;
    }
    if (!data || data.length === 0) {
      setFeedback({ text: "No se pudo eliminar (¿sesión expirada?).", ok: false });
      return;
    }
    void refresh();
  }

  return (
    <div className="card" id="sec-evaluations">
      <h2>
        Evaluaciones
        <span className="pill">{rows.length}</span>
      </h2>
      <p className="muted-note">
        Crea evaluaciones por sección. Activa la evaluación para que los alumnos la vean al arrancar.
      </p>
      <div className="row-flex">
        <div className="field" style={{ flex: "0 0 180px" }}>
          <label htmlFor="evalSection">Sección</label>
          <select
            id="evalSection"
            value={sectionId}
            onChange={(e) => setSectionId(e.target.value)}
          >
            <option value="">Seleccionar sección...</option>
            {sections.map((s) => (
              <option key={s.id} value={s.id}>
                {courseMap.get(s.course_id) ?? "?"} / {s.code}
              </option>
            ))}
          </select>
        </div>
        <div className="field" style={{ flex: "0 0 160px" }}>
          <label htmlFor="evalTitle">Título</label>
          <input
            type="text"
            id="evalTitle"
            placeholder="Evaluación 1"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
          />
        </div>
        <div className="field" style={{ flex: "0 0 200px" }}>
          <label htmlFor="evalOrg">Org GitHub (opcional)</label>
          <input
            type="text"
            id="evalOrg"
            placeholder="Fundamentos-de-la-Programacion"
            value={org}
            onChange={(e) => setOrg(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="evalUrl">URL Classroom (opcional)</label>
          <input
            type="text"
            id="evalUrl"
            placeholder="https://classroom.github.com/a/XXXX"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            onKeyDown={(e) => { if (e.key === "Enter") void addEvaluation(); }}
          />
        </div>
        <button className="btn-primary" onClick={addEvaluation}>
          Agregar evaluación
        </button>
      </div>
      {feedback ? <div className={feedback.ok ? "ok" : "err"}>{feedback.text}</div> : null}

      {loading && rows.length === 0 ? (
        <p style={{ marginTop: 16, color: "var(--text-faint)" }}>Cargando...</p>
      ) : error ? (
        <p className="err" style={{ marginTop: 16 }}>Error: {error}</p>
      ) : (
        <table style={{ marginTop: 16 }}>
          <thead>
            <tr>
              <th style={{ width: "25%" }}>Título</th>
              <th style={{ width: "20%" }}>Sección</th>
              <th style={{ width: "25%" }}>URL</th>
              <th style={{ width: "12%" }}>Estado</th>
              <th style={{ width: "18%" }}>Acciones</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={5} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                  Sin evaluaciones configuradas.
                </td>
              </tr>
            ) : (
              rows.map((e) => {
                const sec = sectionMap.get(e.section_id);
                const courseCode = sec ? courseMap.get(sec.course_id) ?? "?" : "?";
                return (
                  <tr key={e.id}>
                    <td>{e.title}</td>
                    <td>
                      <Badge solidColor={BADGE.user}>
                        {courseCode} / {sec?.code ?? "?"}
                      </Badge>
                    </td>
                    <td>
                      {e.classroom_url ? (
                        <a
                          href={e.classroom_url}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="mono"
                          style={{ fontSize: 12 }}
                        >
                          {e.classroom_url}
                        </a>
                      ) : (
                        <span style={{ color: "var(--text-faint)" }}>—</span>
                      )}
                    </td>
                    <td>
                      <Badge solidColor={e.active ? BADGE.success : BADGE.neutral}>
                        {e.active ? "ACTIVA" : "inactiva"}
                      </Badge>
                    </td>
                    <td>
                      <button
                        className={e.active ? "btn-secondary" : "btn-success"}
                        style={{ padding: "4px 12px", fontSize: 12, height: "auto" }}
                        onClick={() => toggleEvaluation(e.id, !e.active)}
                      >
                        {e.active ? "Desactivar" : "Activar"}
                      </button>
                      <button
                        className="btn-danger"
                        style={{ padding: "4px 10px", fontSize: 14, height: "auto", marginLeft: 6 }}
                        onClick={() => deleteEvaluation(e.id, e.title)}
                      >
                        ×
                      </button>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      )}
      <button className="btn-secondary" style={{ marginTop: 8 }} onClick={refresh}>
        Refrescar
      </button>
    </div>
  );
}
