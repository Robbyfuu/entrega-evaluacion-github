"use client";

import { useMemo, useState } from "react";
import { supabase } from "@/lib/supabase";
import type { SectionRow } from "@/lib/types";
import { useSections } from "@/hooks/useSections";
import { useCourses } from "@/hooks/useCourses";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";

export function SectionsSection() {
  const { rows, loading, error, refresh } = useSections();
  const { rows: courses } = useCourses();
  const [feedback, setFeedback] = useState<{ text: string; ok: boolean } | null>(null);
  const [courseId, setCourseId] = useState<string>("");
  const [code, setCode] = useState("");
  const [name, setName] = useState("");

  const courseMap = useMemo(() => {
    const m = new Map<number, string>();
    for (const c of courses) m.set(c.id, c.code);
    return m;
  }, [courses]);

  async function addSection() {
    const cid = Number(courseId);
    const c = code.trim();
    const n = name.trim();
    if (!cid || !c || !n) {
      setFeedback({ text: "Selecciona curso y completa código y nombre.", ok: false });
      return;
    }
    const { data, error: err } = await supabase
      .from("sections")
      .insert({ course_id: cid, code: c, name: n })
      .select();
    if (err) {
      const duplicate = err.code === "23505" || /duplicate|unique/i.test(err.message);
      setFeedback({
        text: duplicate ? `La sección "${c}" ya existe en ese curso.` : "Error: " + err.message,
        ok: false,
      });
      return;
    }
    if (!data || data.length === 0) {
      setFeedback({ text: "No se pudo agregar (¿sesión expirada?).", ok: false });
      return;
    }
    setCode("");
    setName("");
    setFeedback({ text: `Sección "${c}" agregada.`, ok: true });
    void refresh();
  }

  async function deleteSection(id: SectionRow["id"], code: string) {
    if (!window.confirm(`¿Eliminar la sección "${code}"? Se borrarán sus evaluaciones en cascada.`)) return;
    const { data } = await supabase.from("sections").delete().eq("id", id).select();
    if (!data || data.length === 0) {
      setFeedback({ text: "No se pudo eliminar (¿sesión expirada?).", ok: false });
      return;
    }
    void refresh();
  }

  return (
    <div className="card" id="sec-sections">
      <h2>
        Secciones
        <span className="pill">{rows.length}</span>
      </h2>
      <p className="muted-note">
        Crea secciones bajo cada curso. Las evaluaciones se asignan a una sección específica.
      </p>
      <div className="row-flex">
        <div className="field" style={{ flex: "0 0 180px" }}>
          <label htmlFor="sectionCourse">Curso</label>
          <select
            id="sectionCourse"
            value={courseId}
            onChange={(e) => setCourseId(e.target.value)}
          >
            <option value="">Seleccionar curso...</option>
            {courses.map((c) => (
              <option key={c.id} value={c.id}>
                {c.code} — {c.name}
              </option>
            ))}
          </select>
        </div>
        <div className="field" style={{ flex: "0 0 120px" }}>
          <label htmlFor="sectionCode">Código</label>
          <input
            type="text"
            id="sectionCode"
            placeholder="001D"
            value={code}
            onChange={(e) => setCode(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="sectionName">Nombre</label>
          <input
            type="text"
            id="sectionName"
            placeholder="Sección 001D"
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => { if (e.key === "Enter") void addSection(); }}
          />
        </div>
        <button className="btn-primary" onClick={addSection}>
          Agregar sección
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
              <th style={{ width: "20%" }}>Curso</th>
              <th style={{ width: "20%" }}>Código</th>
              <th style={{ width: "40%" }}>Nombre</th>
              <th style={{ width: "20%" }}>Acciones</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={4} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                  Sin secciones configuradas.
                </td>
              </tr>
            ) : (
              rows.map((s) => (
                <tr key={s.id}>
                  <td>
                    <Badge solidColor={BADGE.sectionAlt}>
                      {courseMap.get(s.course_id) ?? "?"}
                    </Badge>
                  </td>
                  <td className="mono">{s.code}</td>
                  <td>{s.name}</td>
                  <td>
                    <button
                      className="btn-danger"
                      style={{ padding: "4px 10px", fontSize: 14, height: "auto" }}
                      onClick={() => deleteSection(s.id, s.code)}
                    >
                      ×
                    </button>
                  </td>
                </tr>
              ))
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
