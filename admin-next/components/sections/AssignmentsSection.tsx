"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { supabase } from "@/lib/supabase";
import type { AssignmentRow, AssignmentAcceptanceRow, EvaluationRow } from "@/lib/types";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { useEvaluations } from "@/hooks/useEvaluations";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";

// Tareas de GitHub Classroom: assignments CRUD + acceptance cross-reference.
export function AssignmentsSection() {
  const [rows, setRows] = useState<AssignmentRow[]>([]);
  const [acceptances, setAcceptances] = useState<AssignmentAcceptanceRow[]>([]);
  const [feedback, setFeedback] = useState<{ text: string; ok: boolean } | null>(null);

  const [title, setTitle] = useState("");
  const [section, setSection] = useState("");
  const [org, setOrg] = useState("");
  const [url, setUrl] = useState("");
  const [evaluationId, setEvaluationId] = useState<string>("");

  const { sections, sectionById, courseById } = useSectionLookup();
  const { rows: evaluations } = useEvaluations();

  // Section codes come from the DB (dynamic) plus any extra value seen in
  // existing rows, so the editor never hides a row.
  const sectionCodes = useMemo(() => {
    const codes = new Set<string>();
    for (const s of sections) codes.add(s.code);
    for (const r of rows) if (r.section) codes.add(r.section);
    return Array.from(codes).sort();
  }, [sections, rows]);

  // Evaluations grouped by section_id for cascading select.
  const evaluationsBySection = useMemo(() => {
    const m = new Map<number, EvaluationRow[]>();
    for (const e of evaluations) {
      const arr = m.get(e.section_id) ?? [];
      arr.push(e);
      m.set(e.section_id, arr);
    }
    return m;
  }, [evaluations]);

  // When section changes, reset evaluationId if not valid for that section.
  useEffect(() => {
    if (!evaluationId) return;
    const ev = evaluations.find((e) => String(e.id) === evaluationId);
    if (!ev) return;
    // If the selected evaluation's section doesn't match the current section,
    // update the section to match the evaluation (heredity).
    const sec = sectionById.get(ev.section_id);
    if (sec && sec.code !== section) {
      setSection(sec.code);
    }
  }, [evaluationId, evaluations, sectionById, section]);

  const load = useCallback(async () => {
    const { data } = await supabase
      .from("assignments")
      .select("*")
      .order("created_at", { ascending: false });
    setRows((data ?? []) as AssignmentRow[]);

    // Cross-reference acceptances to show how many students accepted each task.
    const { data: acc } = await supabase.from("assignment_acceptances").select("*");
    setAcceptances((acc ?? []) as AssignmentAcceptanceRow[]);
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const acceptanceCount = useMemo(() => {
    const map = new Map<string, number>();
    for (const a of acceptances) {
      const key = String(a.assignment_id);
      map.set(key, (map.get(key) ?? 0) + 1);
    }
    return map;
  }, [acceptances]);

  const sectionLabel = (r: AssignmentRow) => {
    if (r.evaluation_id) {
      const ev = evaluations.find((e) => e.id === r.evaluation_id);
      if (ev) {
        const sec = sectionById.get(ev.section_id);
        if (sec) {
          const course = courseById.get(sec.course_id);
          return `${course?.code ?? "?"}/${sec.code}`;
        }
      }
    }
    return r.section || "Todas";
  };

  async function addAssignment() {
    if (!title.trim() || !url.trim()) {
      setFeedback({ text: "Completa título y URL.", ok: false });
      return;
    }
    const insert: Record<string, unknown> = {
      title: title.trim(),
      classroom_url: url.trim(),
      section,
      org: org.trim(),
      active: true,
    };
    if (evaluationId) {
      insert.evaluation_id = Number(evaluationId);
    }
    const { error: err } = await supabase.from("assignments").insert(insert);
    if (err) {
      setFeedback({ text: "Error: " + err.message, ok: false });
    } else {
      setTitle("");
      setUrl("");
      setOrg("");
      setEvaluationId("");
      setFeedback({ text: "Tarea agregada.", ok: true });
      void load();
    }
  }

  async function toggleAssignment(id: AssignmentRow["id"], active: boolean) {
    await supabase.from("assignments").update({ active }).eq("id", id);
    void load();
  }

  async function deleteAssignment(id: AssignmentRow["id"]) {
    if (!window.confirm("¿Eliminar esta tarea?")) return;
    await supabase.from("assignments").delete().eq("id", id);
    void load();
  }

  // Evaluations available for the currently selected section.
  const availableEvaluations = useMemo(() => {
    if (!section) return evaluations;
    const sec = sections.find((s) => s.code === section);
    if (!sec) return [];
    return evaluationsBySection.get(sec.id) ?? [];
  }, [section, sections, evaluations, evaluationsBySection]);

  return (
    <div className="card" id="sec-tareas">
      <h2>
        Tareas de GitHub Classroom
        <span style={{ fontSize: 12, color: "var(--text-faint)", marginLeft: 8 }}>
          (visibles para los alumnos en su script)
        </span>
      </h2>
      <p className="muted-note">
        Pega los links de assignment de{" "}
        <a href="https://classroom.github.com/" target="_blank" rel="noopener noreferrer">
          classroom.github.com
        </a>
        . Opcional: vincula la tarea a una evaluación para heredar sección y curso.
      </p>
      <div className="row-flex">
        <div className="field" style={{ flex: "0 0 160px" }}>
          <label htmlFor="asgTitle">Título</label>
          <input
            type="text"
            id="asgTitle"
            placeholder="Evaluación 1"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
          />
        </div>
        <div className="field" style={{ flex: "0 0 110px" }}>
          <label htmlFor="asgSection">Sección</label>
          <select
            id="asgSection"
            value={section}
            onChange={(e) => setSection(e.target.value)}
          >
            <option value="">Todas las secciones</option>
            {sectionCodes.map((sec) => (
              <option key={sec} value={sec}>
                {sec}
              </option>
            ))}
          </select>
        </div>
        <div className="field" style={{ flex: "0 0 160px" }}>
          <label htmlFor="asgEvaluation">Evaluación (opcional)</label>
          <select
            id="asgEvaluation"
            value={evaluationId}
            onChange={(e) => setEvaluationId(e.target.value)}
          >
            <option value="">— Ninguna —</option>
            {availableEvaluations.map((ev) => (
              <option key={ev.id} value={ev.id}>
                {ev.title}
              </option>
            ))}
          </select>
        </div>
        <div className="field" style={{ flex: "0 0 200px" }}>
          <label htmlFor="asgOrg">Org GitHub (Classroom)</label>
          <input
            type="text"
            id="asgOrg"
            placeholder="Fundamentos-de-la-Programacion"
            value={org}
            onChange={(e) => setOrg(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="asgUrl">URL del Classroom assignment</label>
          <input
            type="text"
            id="asgUrl"
            placeholder="https://classroom.github.com/a/XXXX"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
          />
        </div>
        <button className="btn-primary" onClick={addAssignment}>
          Agregar tarea
        </button>
      </div>
      {feedback ? <div className={feedback.ok ? "ok" : "err"}>{feedback.text}</div> : null}
      <table style={{ marginTop: 16 }}>
        <thead>
          <tr>
            <th style={{ width: "20%" }}>Título</th>
            <th style={{ width: "14%" }}>Sección</th>
            <th style={{ width: "26%" }}>URL</th>
            <th style={{ width: "10%" }}>Aceptaciones</th>
            <th style={{ width: "13%" }}>Estado</th>
            <th style={{ width: "15%" }}>Acciones</th>
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr>
              <td colSpan={6} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                Sin tareas configuradas. Agrega una pegando el link de Classroom.
              </td>
            </tr>
          ) : (
            rows.map((a) => {
              const label = sectionLabel(a);
              return (
                <tr key={a.id}>
                  <td>{a.title}</td>
                  <td>
                    <Badge solidColor={a.section || a.evaluation_id ? BADGE.user : BADGE.sectionAlt}>
                      {label}
                    </Badge>
                  </td>
                  <td>
                    <a
                      href={a.classroom_url}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="mono"
                      style={{ fontSize: 12 }}
                    >
                      {a.classroom_url}
                    </a>
                  </td>
                  <td className="mono">{acceptanceCount.get(String(a.id)) ?? 0}</td>
                  <td>
                    <Badge solidColor={a.active ? BADGE.success : BADGE.neutral}>
                      {a.active ? "ACTIVA" : "inactiva"}
                    </Badge>
                  </td>
                  <td>
                    <button
                      className={a.active ? "btn-secondary" : "btn-success"}
                      style={{ padding: "4px 12px", fontSize: 12, height: "auto" }}
                      onClick={() => toggleAssignment(a.id, !a.active)}
                    >
                      {a.active ? "Desactivar" : "Activar"}
                    </button>
                    <button
                      className="btn-danger"
                      style={{
                        padding: "4px 10px",
                        fontSize: 14,
                        height: "auto",
                        marginLeft: 6,
                      }}
                      onClick={() => deleteAssignment(a.id)}
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
    </div>
  );
}
