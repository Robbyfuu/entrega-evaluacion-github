"use client";

import type { ReactNode } from "react";
import { Globe, Lock, MonitorSmartphone, TriangleAlert } from "lucide-react";
import type { ControlRow } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { cn } from "@/lib/utils";

interface KpiRowProps {
  control: ControlRow | null;
  onlineCount: number;
  alertCount: number;
}

interface KpiProps {
  label: string;
  value: ReactNode;
  icon: ReactNode;
  accent: "primary" | "warning" | "danger" | "success" | "muted";
}

const ACCENT_RING: Record<KpiProps["accent"], string> = {
  primary: "text-primary",
  warning: "text-amber-500",
  danger: "text-destructive",
  success: "text-emerald-500",
  muted: "text-muted-foreground",
};

function Kpi({ label, value, icon, accent }: KpiProps) {
  return (
    <Card className="gap-0 py-4">
      <CardContent className="flex items-center justify-between gap-3 px-4">
        <div className="min-w-0">
          <p className="text-xs font-medium text-muted-foreground">{label}</p>
          <p className="mt-1 truncate font-mono text-2xl font-bold tabular-nums">{value}</p>
        </div>
        <div className={cn("shrink-0", ACCENT_RING[accent])}>{icon}</div>
      </CardContent>
    </Card>
  );
}

// Top KPI row: live PCs, alerts, internet and lockdown state.
export function KpiRow({ control, onlineCount, alertCount }: KpiRowProps) {
  const netBlocked = !!control?.internet_block;
  const locked = !!control?.force_lockdown;
  const net = control ? (netBlocked ? "BLOQUEADO" : "libre") : "—";
  const lock = control ? (locked ? "ACTIVO" : "inactivo") : "—";

  return (
    <div
      id="sec-kpi"
      className="mb-4 grid scroll-mt-20 grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4"
    >
      <Kpi
        label="PCs conectados"
        value={onlineCount}
        icon={<MonitorSmartphone className="size-6" />}
        accent="primary"
      />
      <Kpi
        label="Alertas (50)"
        value={alertCount}
        icon={<TriangleAlert className="size-6" />}
        accent="warning"
      />
      <Kpi
        label="Internet"
        value={
          <span className={cn("text-lg", netBlocked ? "text-destructive" : "text-emerald-500")}>
            {net}
          </span>
        }
        icon={<Globe className="size-6" />}
        accent={netBlocked ? "danger" : "success"}
      />
      <Kpi
        label="Lockdown"
        value={
          <span className={cn("text-lg", locked ? "text-destructive" : "text-emerald-500")}>
            {lock}
          </span>
        }
        icon={<Lock className="size-6" />}
        accent={locked ? "danger" : "success"}
      />
    </div>
  );
}
