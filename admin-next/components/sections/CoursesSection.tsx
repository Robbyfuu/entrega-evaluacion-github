"use client";

import { useState } from "react";
import { supabase } from "@/lib/supabase";
import type { CourseRow } from "@/lib/types";
import { useCourses } from "@/hooks/useCourses";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";

export function CoursesSection() {
  const { rows, loading, error, refresh } = useCourses();
  const [feedback, setFeedback] = useState<{ text: string; ok: boolean } | null>(null);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");

  async function addCourse() {
    const c = code.trim();
    const n = name.trim();
    if (!c || !n) {
      setFeedback({ text: "Completa código y nombre.", ok: false });
      return;
    }
    const { data, error: err } = await supabase
      .from("courses")
      .insert({ code: c, name: n, active: true })
      .select();
    if (err) {
      const duplicate = err.code === "23505" || /duplicate|unique/i.test(err.message);
      setFeedback({
        text: duplicate ? `El código "${c}" ya existe.` : "Error: " + err.message,
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
    setFeedback({ text: `Curso "${c}" agregado.`, ok: true });
    void refresh();
  }

  async function toggleCourse(id: CourseRow["id"], active: boolean) {
    const { data, error: err } = await supabase.from("courses").update({ active }).eq("id", id).select();
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

  async function deleteCourse(id: CourseRow["id"], code: string) {
    if (!window.confirm(`¿Eliminar el curso "${code}"? Se borrarán sus secciones y evaluaciones en cascada.`)) return;
    const { data, error: err } = await supabase.from("courses").delete().eq("id", id).select();
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
    <div className="card" id="sec-courses">
      <h2>
        Cursos
        <span className="pill">{rows.length}</span>
      </h2>
      <p className="muted-note">
        Crea y administra los cursos. Cada curso agrupa secciones y evaluaciones.
      </p>
      <div className="row-flex">
        <div className="field" style={{ flex: "0 0 140px" }}>
          <label htmlFor="courseCode">Código</label>
          <input
            type="text"
            id="courseCode"
            placeholder="FPY1101"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            onKeyDown={(e) => { if (e.key === "Enter") void addCourse(); }}
          />
        </div>
        <div className="field">
          <label htmlFor="courseName">Nombre</label>
          <input
            type="text"
            id="courseName"
            placeholder="Física I"
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => { if (e.key === "Enter") void addCourse(); }}
          />
        </div>
        <button className="btn-primary" onClick={addCourse}>
          Agregar curso
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
              <th style={{ width: "20%" }}>Código</th>
              <th style={{ width: "40%" }}>Nombre</th>
              <th style={{ width: "15%" }}>Estado</th>
              <th style={{ width: "25%" }}>Acciones</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={4} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                  Sin cursos configurados.
                </td>
              </tr>
            ) : (
              rows.map((c) => (
                <tr key={c.id}>
                  <td className="mono">{c.code}</td>
                  <td>{c.name}</td>
                  <td>
                    <Badge solidColor={c.active ? BADGE.success : BADGE.neutral}>
                      {c.active ? "ACTIVO" : "inactivo"}
                    </Badge>
                  </td>
                  <td>
                    <button
                      className={c.active ? "btn-secondary" : "btn-success"}
                      style={{ padding: "4px 12px", fontSize: 12, height: "auto" }}
                      onClick={() => toggleCourse(c.id, !c.active)}
                    >
                      {c.active ? "Desactivar" : "Activar"}
                    </button>
                    <button
                      className="btn-danger"
                      style={{ padding: "4px 10px", fontSize: 14, height: "auto", marginLeft: 6 }}
                      onClick={() => deleteCourse(c.id, c.code)}
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
