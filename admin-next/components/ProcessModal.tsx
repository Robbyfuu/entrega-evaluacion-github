"use client";

import { useEffect } from "react";
import type { OnlineClientRow } from "@/lib/types";
import { isSuspicious } from "@/lib/suspicious";

interface ProcessModalProps {
  client: OnlineClientRow | null;
  onClose: () => void;
}

// Modal listing the open processes of a single PC. Replicates #processModal.
export function ProcessModal({ client, onClose }: ProcessModalProps) {
  useEffect(() => {
    if (!client) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [client, onClose]);

  if (!client) return null;

  const procs = Array.isArray(client.processes) ? client.processes : [];

  return (
    <div
      className="modal-overlay"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="modal-box">
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: 16,
          }}
        >
          <h2 style={{ margin: 0 }}>
            Programas abiertos en {client.pc_name} (@{client.github_username || "?"})
          </h2>
          <button className="btn-secondary" onClick={onClose}>
            Cerrar ✕
          </button>
        </div>
        <table>
          <thead>
            <tr>
              <th>Proceso</th>
              <th>Título de ventana</th>
            </tr>
          </thead>
          <tbody>
            {procs.length === 0 ? (
              <tr>
                <td colSpan={2} style={{ textAlign: "center", color: "var(--text-faint)" }}>
                  Sin procesos con ventana visible.
                </td>
              </tr>
            ) : (
              procs.map((p, i) => {
                const susp = isSuspicious(p.name);
                return (
                  <tr key={i} className={susp ? "row-blocked" : undefined}>
                    <td
                      style={
                        susp ? { color: "var(--danger)", fontWeight: 600 } : undefined
                      }
                    >
                      {susp ? `⚠ ${p.name}` : p.name || "-"}
                    </td>
                    <td>{p.title || "-"}</td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
