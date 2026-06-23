"use client";

import {
  Activity,
  CheckCircle2,
  MonitorSmartphone,
  ShieldAlert,
  ShieldCheck,
  TriangleAlert,
} from "lucide-react";
import type { ControlRow } from "@/lib/types";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/Badge";
import { KpiRow } from "@/components/sections/KpiRow";
import { cn } from "@/lib/utils";

interface OverviewSectionProps {
  control: ControlRow | null;
  onlineCount: number;
  alertCount: number;
}

// Resumen operativo: grilla de KPIs + estado del sistema + tarjetas de
// contexto (alertas / PCs) que reutilizan los MISMOS contadores que ya
// llegan por props. No agrega queries ni hooks: solo presentacion.
export function OverviewSection({ control, onlineCount, alertCount }: OverviewSectionProps) {
  const netBlocked = !!control?.internet_block;
  const locked = !!control?.force_lockdown;
  const anyBlock = netBlocked || locked;
  const hasAlerts = alertCount > 0;
  const hasOnline = onlineCount > 0;

  return (
    <div className="space-y-4">
      <div>
        <h2 className="font-mono text-base font-semibold tracking-tight text-foreground">
          Resumen
        </h2>
        <p className="text-sm text-muted-foreground">
          Vista operativa de un vistazo: estado del bloqueo, alertas y equipos conectados.
        </p>
      </div>

      <KpiRow control={control} onlineCount={onlineCount} alertCount={alertCount} />

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        {/* Estado del sistema — refleja el control global */}
        <Card className="lg:col-span-1">
          <CardHeader className="pb-2">
            <CardTitle className="flex items-center gap-2 text-sm">
              {anyBlock ? (
                <ShieldAlert className="size-4 text-destructive" />
              ) : (
                <ShieldCheck className="size-4 text-emerald-600 dark:text-emerald-400" />
              )}
              Estado del sistema
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Bloqueo global</span>
              {!control ? (
                <Badge variant="neutral">sin datos</Badge>
              ) : anyBlock ? (
                <Badge variant="cheat">bloqueo activo</Badge>
              ) : (
                <Badge variant="success">normal</Badge>
              )}
            </div>

            <div className="flex items-center justify-between border-t border-border pt-3">
              <span className="text-sm text-muted-foreground">Internet</span>
              <span
                className={cn(
                  "font-mono text-sm font-semibold tabular-nums",
                  netBlocked ? "text-destructive" : "text-emerald-600 dark:text-emerald-400",
                )}
              >
                {control ? (netBlocked ? "BLOQUEADO" : "Libre") : "—"}
              </span>
            </div>

            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Lockdown</span>
              <span
                className={cn(
                  "font-mono text-sm font-semibold tabular-nums",
                  locked ? "text-destructive" : "text-emerald-600 dark:text-emerald-400",
                )}
              >
                {control ? (locked ? "ACTIVO" : "Inactivo") : "—"}
              </span>
            </div>
          </CardContent>
        </Card>

        {/* Ultimas alertas — placeholder con el contador real */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="flex items-center gap-2 text-sm">
              <TriangleAlert
                className={cn(
                  "size-4",
                  hasAlerts
                    ? "text-amber-600 dark:text-amber-400"
                    : "text-muted-foreground",
                )}
              />
              Ultimas alertas
            </CardTitle>
          </CardHeader>
          <CardContent>
            {hasAlerts ? (
              <div className="space-y-1">
                <p className="font-mono text-3xl font-bold leading-none tabular-nums text-amber-600 dark:text-amber-400">
                  {alertCount}
                </p>
                <p className="text-sm text-muted-foreground">
                  alertas de procesos activas. Abre la seccion{" "}
                  <span className="font-medium text-foreground">Alertas</span> para ver el detalle.
                </p>
              </div>
            ) : (
              <div className="flex items-start gap-2">
                <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-emerald-600 dark:text-emerald-400" />
                <p className="text-sm text-muted-foreground">
                  Sin alertas de procesos por ahora.
                </p>
              </div>
            )}
          </CardContent>
        </Card>

        {/* PCs conectados — placeholder con el contador real */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="flex items-center gap-2 text-sm">
              <MonitorSmartphone
                className={cn(
                  "size-4",
                  hasOnline
                    ? "text-emerald-600 dark:text-emerald-400"
                    : "text-muted-foreground",
                )}
              />
              PCs conectados
            </CardTitle>
          </CardHeader>
          <CardContent>
            {hasOnline ? (
              <div className="space-y-1">
                <p className="font-mono text-3xl font-bold leading-none tabular-nums text-foreground">
                  {onlineCount}
                </p>
                <p className="text-sm text-muted-foreground">
                  equipos en linea. Abre la seccion{" "}
                  <span className="font-medium text-foreground">PCs conectados</span> para
                  gestionarlos.
                </p>
              </div>
            ) : (
              <div className="flex items-start gap-2">
                <Activity className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                <p className="text-sm text-muted-foreground">
                  Ningun equipo conectado en este momento.
                </p>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
