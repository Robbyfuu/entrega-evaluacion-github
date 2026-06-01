"use client";

import { useState } from "react";
import { supabase } from "@/lib/supabase";
import type { ControlRow } from "@/lib/types";
import { fmt } from "@/lib/format";

interface ControlSectionProps {
  control: ControlRow | null;
  error: string | null;
  onRefresh: () => void;
}

type Patch = Partial<Pick<ControlRow, "internet_block" | "force_lockdown" | "message">>;

// "Estado actual" + "Controles remotos" cards. Writes to control row id=1.
export function ControlSection({ control, error, onRefresh }: ControlSectionProps) {
  const [feedback, setFeedback] = useState<{ text: string; ok: boolean } | null>(null);
  const [msgInput, setMsgInput] = useState("");

  async function setControl(patch: Patch) {
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

  function confirmLockdown() {
    if (
      window.confirm(
        "Esto activará el lockdown rojo en TODOS los PCs conectados. ¿Confirmar?"
      )
    ) {
      void setControl({ force_lockdown: true });
    }
  }

  async function sendMessage() {
    const txt = msgInput.trim();
    if (!txt) {
      setFeedback({ text: "Escribe un mensaje.", ok: false });
      return;
    }
    await setControl({ message: txt });
    setMsgInput("");
  }

  return (
    <>
      {/* Estado actual */}
      <div className="card">
        <h2>Estado actual</h2>
        <div className="status-grid">
          <div className="status-label">Internet:</div>
          <div className="status-value">
            {error ? (
              "Error: " + error
            ) : control ? (
              <span className={control.internet_block ? "status-on" : "status-off"}>
                {control.internet_block ? "BLOQUEADO" : "libre"}
              </span>
            ) : (
              "..."
            )}
          </div>
          <div className="status-label">Lockdown remoto:</div>
          <div className="status-value">
            {control ? (
              <span className={control.force_lockdown ? "status-on" : "status-off"}>
                {control.force_lockdown ? "ACTIVO" : "inactivo"}
              </span>
            ) : (
              "..."
            )}
          </div>
          <div className="status-label">Mensaje activo:</div>
          <div className="status-value">{control?.message || "(ninguno)"}</div>
          <div className="status-label">Última actualización:</div>
          <div className="status-value">
            {control
              ? fmt(control.updated_at) +
                (control.updated_by ? ` por ${control.updated_by}` : "")
              : "..."}
          </div>
        </div>
        <button className="btn-secondary" style={{ marginTop: 16 }} onClick={onRefresh}>
          Refrescar
        </button>
      </div>

      {/* Controles remotos */}
      <div className="card" id="sec-control">
        <h2>Controles remotos (afectan a todos los alumnos en menos de 60s)</h2>
        <div className="btn-row">
          <button className="btn-danger" onClick={() => setControl({ internet_block: true })}>
            Bloquear internet
          </button>
          <button
            className="btn-success"
            onClick={() => setControl({ internet_block: false })}
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
            onClick={() => setControl({ force_lockdown: false })}
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
          <button className="btn-secondary" onClick={() => setControl({ message: "" })}>
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
