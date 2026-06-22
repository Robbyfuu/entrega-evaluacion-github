"use client";

import { useState } from "react";
import { supabase } from "@/lib/supabase";
import type { ControlRow, EvaluationControlRow } from "@/lib/types";
import { fmt } from "@/lib/format";
import { EvaluationSelector } from "@/components/sections/EvaluationSelector";

interface ControlSectionProps {
  // Global control (id=1) — legacy blast-radius path, affects ALL students.
  control: ControlRow | null;
  error: string | null;
  onRefresh: () => void;
  // Per-evaluation selection + override. When selectedEvaluationId is non-null,
  // the panel controls one evaluation and the global toggle is hidden.
  selectedEvaluationId: number | null;
  onSelectEvaluation: (evaluationId: number | null) => void;
  evalControl: EvaluationControlRow | null;
  evalControlError: string | null;
  setEvaluationControl: (
    patch: Partial<
      Pick<EvaluationControlRow, "internet_block" | "force_lockdown" | "message">
    >
  ) => Promise<{ ok: boolean; error?: string }>;
}

type Patch = Partial<Pick<ControlRow, "internet_block" | "force_lockdown" | "message">>;

// Resolve a per-eval override field over the global control: override ?? global.
// Mirrors the C# resolver (override ?? global control id=1) for display only.
function resolveBool(
  override: boolean | null | undefined,
  global: boolean | undefined
): boolean {
  return (override ?? global) ?? false;
}

function resolveMessage(
  override: string | null | undefined,
  global: string | null | undefined
): string {
  return (override ?? global) ?? "";
}

// "Estado actual" + "Controles remotos" cards.
//
// Two control paths:
//  - GLOBAL (no evaluation selected): writes to control id=1, affects ALL
//    students in every section. Legacy / fallback path.
//  - PER-EVALUATION (an evaluation selected): writes an override row in
//    evaluation_control scoped to that one evaluation.
//
// CRITICAL (mixed-client safety, correction 5): while a per-evaluation is
// selected, the global blast-radius controls are HIDDEN so the teacher cannot
// accidentally freeze every section with the global toggle while running
// concurrent evaluations.
export function ControlSection({
  control,
  error,
  onRefresh,
  selectedEvaluationId,
  onSelectEvaluation,
  evalControl,
  evalControlError,
  setEvaluationControl,
}: ControlSectionProps) {
  const [feedback, setFeedback] = useState<{ text: string; ok: boolean } | null>(null);
  const [msgInput, setMsgInput] = useState("");

  const perEvalActive = selectedEvaluationId != null;

  async function setGlobalControl(patch: Patch) {
    const {
      data: { user },
    } = await supabase.auth.getUser();
    const { error: err } = await supabase
      .from("control")
      .update({ ...patch, updated_by: user?.email ?? null })
      .eq("id", 1);
    if (err) {
      setFeedback({ text: "Error: " + err.message, ok: false });
    } else {
      setFeedback({ text: "Actualizado. Los alumnos lo verán en <60s.", ok: true });
      onRefresh();
    }
  }

  async function setEvalControl(patch: Patch) {
    const res = await setEvaluationControl(patch);
    if (!res.ok) {
      setFeedback({ text: "Error: " + (res.error ?? "desconocido"), ok: false });
    } else {
      setFeedback({
        text: "Override de la evaluación actualizado. Los alumnos lo verán en <60s.",
        ok: true,
      });
    }
  }

  // Route a control change to the active path (per-eval override or global).
  async function applyControl(patch: Patch) {
    if (perEvalActive) {
      await setEvalControl(patch);
    } else {
      await setGlobalControl(patch);
    }
  }

  function confirmLockdown() {
    const warning = perEvalActive
      ? "Esto activará el lockdown rojo en los PCs de esta evaluación. ¿Confirmar?"
      : "Esto activará el lockdown rojo en TODOS los PCs conectados. ¿Confirmar?";
    if (window.confirm(warning)) {
      void applyControl({ force_lockdown: true });
    }
  }

  async function sendMessage() {
    const txt = msgInput.trim();
    if (!txt) {
      setFeedback({ text: "Escribe un mensaje.", ok: false });
      return;
    }
    await applyControl({ message: txt });
    setMsgInput("");
  }

  // Effective state shown in "Estado actual":
  //  - per-eval: override ?? global (resolved, as the client will see it)
  //  - global:   the control id=1 row
  const effInternet = perEvalActive
    ? resolveBool(evalControl?.internet_block, control?.internet_block)
    : control?.internet_block ?? false;
  const effLockdown = perEvalActive
    ? resolveBool(evalControl?.force_lockdown, control?.force_lockdown)
    : control?.force_lockdown ?? false;
  const effMessage = perEvalActive
    ? resolveMessage(evalControl?.message, control?.message)
    : control?.message ?? "";
  const statusError = perEvalActive ? evalControlError : error;
  const updatedSource = perEvalActive ? evalControl : control;

  return (
    <>
      {/* Selector de evaluación a controlar */}
      <div className="card" id="sec-control-selector">
        <h2>Alcance del control</h2>
        <p className="muted-note">
          Elige una evaluación para controlarla de forma aislada (no afecta a
          otras secciones que rinden en paralelo). Deja{" "}
          <strong>Control global</strong> solo si quieres afectar a TODAS las
          secciones a la vez.
        </p>
        <div className="row-flex">
          <EvaluationSelector
            selectedEvaluationId={selectedEvaluationId}
            onSelect={onSelectEvaluation}
          />
        </div>
        {perEvalActive ? (
          <div className="ok" style={{ marginTop: 12 }}>
            Controlando solo esta evaluación. El control global queda oculto para
            evitar congelar otras secciones por error.
          </div>
        ) : null}
      </div>

      {/* Estado actual */}
      <div className="card">
        <h2>
          Estado actual
          {perEvalActive ? (
            <span className="pill" style={{ marginLeft: 8 }}>
              por evaluación
            </span>
          ) : (
            <span className="pill" style={{ marginLeft: 8 }}>
              global
            </span>
          )}
        </h2>
        <div className="status-grid">
          <div className="status-label">Internet:</div>
          <div className="status-value">
            {statusError ? (
              "Error: " + statusError
            ) : control || updatedSource ? (
              <span className={effInternet ? "status-on" : "status-off"}>
                {effInternet ? "BLOQUEADO" : "libre"}
              </span>
            ) : (
              "..."
            )}
          </div>
          <div className="status-label">Lockdown remoto:</div>
          <div className="status-value">
            <span className={effLockdown ? "status-on" : "status-off"}>
              {effLockdown ? "ACTIVO" : "inactivo"}
            </span>
          </div>
          <div className="status-label">Mensaje activo:</div>
          <div className="status-value">{effMessage || "(ninguno)"}</div>
          <div className="status-label">Última actualización:</div>
          <div className="status-value">
            {updatedSource
              ? fmt(updatedSource.updated_at) +
                (updatedSource.updated_by ? ` por ${updatedSource.updated_by}` : "")
              : perEvalActive
                ? "(sin override aún — hereda del global)"
                : "..."}
          </div>
        </div>
        <button className="btn-secondary" style={{ marginTop: 16 }} onClick={onRefresh}>
          Refrescar
        </button>
      </div>

      {/* Controles remotos */}
      <div className="card" id="sec-control">
        <h2>
          {perEvalActive
            ? "Controles de esta evaluación (afectan solo a sus alumnos en menos de 60s)"
            : "Controles remotos (afectan a todos los alumnos en menos de 60s)"}
        </h2>

        {perEvalActive ? (
          <p className="muted-note">
            El control global (que afecta a TODAS las secciones) está oculto
            mientras controlas una evaluación específica. Esto evita congelar
            otras secciones que rinden en paralelo. Recuerda: el bloqueo por
            evaluación solo es confiable cuando TODOS los PCs de esa sección ya
            corren el cliente actualizado; mientras quede un PC con cliente
            antiguo, ese PC lee solo el control global.
          </p>
        ) : null}

        <div className="btn-row">
          <button
            className="btn-danger"
            onClick={() => applyControl({ internet_block: true })}
          >
            Bloquear internet
          </button>
          <button
            className="btn-success"
            onClick={() => applyControl({ internet_block: false })}
          >
            Desbloquear internet
          </button>
          <button
            className="btn-danger"
            style={{ background: "#b71c1c" }}
            onClick={confirmLockdown}
          >
            LOCKDOWN remoto
          </button>
          <button
            className="btn-secondary"
            onClick={() => applyControl({ force_lockdown: false })}
          >
            Liberar lockdown
          </button>
        </div>

        <div className="row-flex" style={{ marginTop: 24 }}>
          <div className="field">
            <label htmlFor="msgInput">Mensaje al aula</label>
            <input
              type="text"
              id="msgInput"
              placeholder="Ej: Quedan 10 minutos para entregar"
              value={msgInput}
              onChange={(e) => setMsgInput(e.target.value)}
            />
          </div>
          <button className="btn-primary" onClick={sendMessage}>
            Enviar mensaje
          </button>
          <button
            className="btn-secondary"
            onClick={() => applyControl({ message: "" })}
          >
            Borrar mensaje
          </button>
        </div>
        {feedback ? (
          <div className={feedback.ok ? "ok" : "err"}>{feedback.text}</div>
        ) : null}
      </div>
    </>
  );
}
