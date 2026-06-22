"use client";

import { useEffect } from "react";
import type { ProcessAlertRow } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { fmt } from "@/lib/format";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";
import { DataTable, type Column } from "@/components/ui/DataTable";

interface ProcessAlertsSectionProps {
  onCountChange: (count: number) => void;
}

// Alertas de procesos sospechosos (últimas 50). Live via process_alerts.
export function ProcessAlertsSection({ onCountChange }: ProcessAlertsSectionProps) {
  const { rows, loading, error, refresh } = useRealtimeTable<
    ProcessAlertRow & Record<string, unknown>
  >({
    table: "process_alerts",
    order: { column: "detected_at", ascending: false },
    limit: 50,
    getId: (r) => r.id ?? `${r.detected_at}|${r.pc_name}|${r.process_name}`,
  });

  const { sectionCodeById } = useSectionLookup();

  useEffect(() => {
    onCountChange(rows.length);
  }, [rows.length, onCountChange]);

  const columns: Column<ProcessAlertRow>[] = [
    { header: "Fecha", cell: (a) => fmt(a.detected_at) },
    {
      header: "Usuario",
      cell: (a) =>
        a.github_username ? (
          <Badge solidColor={BADGE.user}>@{a.github_username}</Badge>
        ) : (
          "-"
        ),
    },
    { header: "PC", cell: (a) => a.pc_name || "-" },
    { header: "Sección", cell: (a) => (sectionCodeById(a.section_id) ?? a.section) || "-" },
    {
      header: "Proceso",
      cell: (a) => <Badge solidColor={BADGE.danger}>{a.process_name}</Badge>,
    },
    { header: "Título ventana", cell: (a) => a.window_title || "-" },
  ];

  return (
    <div className="card" id="sec-alerts">
      <h2>
        ⚠ Alertas de procesos sospechosos
        <span style={{ fontSize: 12, color: "var(--text-faint)", marginLeft: 8 }}>
          (últimas 50)
        </span>
        <span className="pill pill-danger">{rows.length}</span>
      </h2>
      <p className="muted-note">
        Se dispara cuando un alumno abre browsers, mensajeros, IDEs alternos, terminales o
        software de acceso remoto durante la sesión.
      </p>
      <DataTable
        columns={columns}
        rows={rows}
        getRowKey={(a, i) => a.id ?? i}
        loading={loading}
        error={error}
        emptyMessage="Sin alertas."
      />
      <button className="btn-secondary" style={{ marginTop: 16 }} onClick={refresh}>
        Refrescar
      </button>
    </div>
  );
}
