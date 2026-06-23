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

type Accent = "primary" | "warning" | "danger" | "success" | "muted";

interface KpiProps {
  label: string;
  value: ReactNode;
  icon: ReactNode;
  accent: Accent;
}

// Icon tint + top accent bar per state. Green = healthy, amber = attention,
// red = active block/alert, blue = neutral metric.
const ACCENT_ICON: Record<Accent, string> = {
  primary: "text-primary",
  warning: "text-amber-600 dark:text-amber-400",
  danger: "text-destructive",
  success: "text-emerald-600 dark:text-emerald-400",
  muted: "text-muted-foreground",
};

const ACCENT_BAR: Record<Accent, string> = {
  primary: "bg-primary",
  warning: "bg-amber-500",
  danger: "bg-destructive",
  success: "bg-emerald-500",
  muted: "bg-muted-foreground/40",
};

const ACCENT_VALUE: Record<Accent, string> = {
  primary: "text-foreground",
  warning: "text-amber-600 dark:text-amber-400",
  danger: "text-destructive",
  success: "text-emerald-600 dark:text-emerald-400",
  muted: "text-foreground",
};

function Kpi({ label, value, icon, accent }: KpiProps) {
  return (
    <Card className="group relative gap-0 overflow-hidden py-0 transition-colors duration-200 hover:bg-muted/40">
      <div className={cn("absolute inset-x-0 top-0 h-0.5", ACCENT_BAR[accent])} />
      <CardContent className="flex flex-col gap-2 p-4">
        <div className="flex items-center justify-between">
          <span className="text-[0.7rem] font-semibold uppercase tracking-wider text-muted-foreground">
            {label}
          </span>
          <span className={cn("shrink-0", ACCENT_ICON[accent])}>{icon}</span>
        </div>
        <span
          className={cn(
            "font-mono text-3xl font-bold leading-none tabular-nums",
            ACCENT_VALUE[accent],
          )}
        >
          {value}
        </span>
      </CardContent>
    </Card>
  );
}

// Top KPI row: live PCs, alerts, internet and lockdown state.
export function KpiRow({ control, onlineCount, alertCount }: KpiRowProps) {
  const netBlocked = !!control?.internet_block;
  const locked = !!control?.force_lockdown;
  const net = control ? (netBlocked ? "BLOQUEADO" : "Libre") : "—";
  const lock = control ? (locked ? "ACTIVO" : "Inactivo") : "—";

  const hasAlerts = alertCount > 0;
  const hasOnline = onlineCount > 0;

  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      <Kpi
        label="PCs conectados"
        value={onlineCount}
        icon={<MonitorSmartphone className="size-5" />}
        accent={hasOnline ? "success" : "muted"}
      />
      <Kpi
        label="Alertas (50)"
        value={alertCount}
        icon={<TriangleAlert className="size-5" />}
        accent={hasAlerts ? "warning" : "muted"}
      />
      <Kpi
        label="Internet"
        value={<span className="text-xl">{net}</span>}
        icon={<Globe className="size-5" />}
        accent={!control ? "muted" : netBlocked ? "danger" : "success"}
      />
      <Kpi
        label="Lockdown"
        value={<span className="text-xl">{lock}</span>}
        icon={<Lock className="size-5" />}
        accent={!control ? "muted" : locked ? "danger" : "success"}
      />
    </div>
  );
}
