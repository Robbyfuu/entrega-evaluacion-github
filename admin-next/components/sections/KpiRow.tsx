"use client";

import type { ControlRow } from "@/lib/types";
import { KpiCard } from "@/components/ui/KpiCard";

interface KpiRowProps {
  control: ControlRow | null;
  onlineCount: number;
  alertCount: number;
}

// Top KPI row: live PCs, alerts, internet and lockdown state.
export function KpiRow({ control, onlineCount, alertCount }: KpiRowProps) {
  const net = control ? (control.internet_block ? "BLOQUEADO" : "libre") : "—";
  const lock = control ? (control.force_lockdown ? "ACTIVO" : "inactivo") : "—";

  return (
    <div className="kpi-row" id="sec-kpi">
      <KpiCard label="PCs conectados" value={onlineCount} variant="primary" />
      <KpiCard label="Alertas (50)" value={alertCount} variant="warning" />
      <KpiCard
        label="Internet"
        value={
          <span className={control?.internet_block ? "status-on" : "status-off"}>{net}</span>
        }
        small
        variant={control?.internet_block ? "danger" : undefined}
      />
      <KpiCard
        label="Lockdown"
        value={
          <span className={control?.force_lockdown ? "status-on" : "status-off"}>{lock}</span>
        }
        small
        variant={control?.force_lockdown ? "danger" : undefined}
      />
    </div>
  );
}
