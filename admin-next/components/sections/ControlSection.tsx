"use client";

import { useEffect, useState } from "react";
import { CheckCircle2, RefreshCw, ShieldAlert, XCircle } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type { ControlRow, EvaluationControlRow } from "@/lib/types";
import { fmt } from "@/lib/format";
import { EvaluationSelector } from "@/components/sections/EvaluationSelector";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Separator } from "@/components/ui/separator";
import { cn } from "@/lib/utils";

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

  // Reset transient UI when the control scope changes so a banner/half-typed
  // message from the previous scope cannot mislead about which scope was just
  // acted on (e.g. a per-eval "Override actualizado" lingering after switching
  // to global). Clearing on scope change keeps feedback scoped to its action.
  useEffect(() => {
    setFeedback(null);
    setMsgInput("");
  }, [selectedEvaluationId]);

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
      ? "Esto activará la pantalla roja en los PCs de esta evaluación. ¿Confirmar?"
      : "Esto activará la pantalla roja en TODOS los PCs conectados. ¿Confirmar?";
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
  const hasState = !!(control || updatedSource);

  return (
    <div className="mb-4 flex flex-col gap-4">
      {/* Selector de evaluación a controlar */}
      <Card id="sec-control-selector" className="scroll-mt-20">
        <CardHeader>
          <CardTitle>Alcance del control</CardTitle>
          <CardDescription>
            Elige una evaluación para controlarla de forma aislada (no afecta a
            otras secciones que rinden en paralelo). Deja{" "}
            <strong className="font-semibold text-foreground">Control global</strong>{" "}
            solo si quieres afectar a TODAS las secciones a la vez.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          <EvaluationSelector
            selectedEvaluationId={selectedEvaluationId}
            onSelect={onSelectEvaluation}
          />
          {perEvalActive ? (
            <div className="flex items-start gap-2 rounded-md border border-emerald-500/30 bg-emerald-500/10 px-3 py-2 text-sm text-emerald-600 dark:text-emerald-400">
              <ShieldAlert className="mt-0.5 size-4 shrink-0" />
              <span>
                Controlando solo esta evaluación. El control global queda oculto
                para evitar congelar otras secciones por error.
              </span>
            </div>
          ) : null}
        </CardContent>
      </Card>

      {/* Estado actual */}
      <Card>
        <CardHeader className="flex flex-row items-center gap-2 space-y-0">
          <CardTitle>Estado actual</CardTitle>
          <Badge variant={perEvalActive ? "info" : "neutral"}>
            {perEvalActive ? "por evaluación" : "global"}
          </Badge>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <dl className="grid grid-cols-1 gap-x-6 gap-y-3 sm:grid-cols-2">
            <StatusItem label="Internet">
              {statusError ? (
                <span className="text-destructive">Error: {statusError}</span>
              ) : hasState ? (
                <span
                  className={cn(
                    "font-semibold",
                    effInternet ? "text-destructive" : "text-emerald-500"
                  )}
                >
                  {effInternet ? "BLOQUEADO" : "libre"}
                </span>
              ) : (
                <span className="text-muted-foreground">…</span>
              )}
            </StatusItem>
            <StatusItem label="Pantalla roja a todos">
              {statusError ? (
                <span className="text-destructive">Error: {statusError}</span>
              ) : hasState ? (
                <span
                  className={cn(
                    "font-semibold",
                    effLockdown ? "text-destructive" : "text-emerald-500"
                  )}
                >
                  {effLockdown ? "ACTIVA" : "inactiva"}
                </span>
              ) : (
                <span className="text-muted-foreground">…</span>
              )}
            </StatusItem>
            <StatusItem label="Mensaje activo">
              {hasState ? (
                effMessage || (
                  <span className="text-muted-foreground">(ninguno)</span>
                )
              ) : (
                <span className="text-muted-foreground">…</span>
              )}
            </StatusItem>
            <StatusItem label="Última actualización">
              {updatedSource ? (
                <span className="text-muted-foreground tabular-nums">
                  {fmt(updatedSource.updated_at)}
                  {updatedSource.updated_by ? ` por ${updatedSource.updated_by}` : ""}
                </span>
              ) : perEvalActive ? (
                <span className="text-muted-foreground">
                  (sin override aún — hereda del global)
                </span>
              ) : (
                <span className="text-muted-foreground">…</span>
              )}
            </StatusItem>
          </dl>
          <div>
            <Button variant="outline" size="sm" onClick={onRefresh}>
              <RefreshCw className="size-4" />
              Refrescar
            </Button>
          </div>
        </CardContent>
      </Card>

      {/* Controles remotos */}
      <Card id="sec-control" className="scroll-mt-20">
        <CardHeader>
          <CardTitle>
            {perEvalActive
              ? "Controles de esta evaluación"
              : "Controles remotos"}
          </CardTitle>
          <CardDescription>
            {perEvalActive
              ? "Afectan solo a los alumnos de esta evaluación en menos de 60s."
              : "Afectan a todos los alumnos en menos de 60s."}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-5">
          {perEvalActive ? (
            <p className="rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-sm text-amber-700 dark:text-amber-400">
              El control global (que afecta a TODAS las secciones) está oculto
              mientras controlas una evaluación específica. Esto evita congelar
              otras secciones que rinden en paralelo. Recuerda: el bloqueo por
              evaluación solo es confiable cuando TODOS los PCs de esa sección ya
              corren el cliente actualizado; mientras quede un PC con cliente
              antiguo, ese PC lee solo el control global.
            </p>
          ) : null}

          {/* Toggles de internet / lockdown */}
          <div className="flex flex-col gap-3 sm:flex-row">
            <div className="flex flex-1 items-center justify-between gap-3 rounded-lg border px-4 py-3">
              <div className="flex flex-col gap-0.5">
                <Label htmlFor="internetToggle">Bloquear internet</Label>
                <span className="text-xs text-muted-foreground">
                  {effInternet ? "Internet bloqueado" : "Internet libre"}
                </span>
              </div>
              <Switch
                id="internetToggle"
                checked={effInternet}
                onCheckedChange={(checked) =>
                  applyControl({ internet_block: checked })
                }
              />
            </div>
            <div className="flex flex-1 items-center justify-between gap-3 rounded-lg border px-4 py-3">
              <div className="flex flex-col gap-0.5">
                <Label htmlFor="lockdownToggle">Pantalla roja a todos</Label>
                <span className="text-xs text-muted-foreground">
                  {effLockdown ? "Pantalla roja activa" : "Pantalla roja inactiva"}
                </span>
              </div>
              <Switch
                id="lockdownToggle"
                checked={effLockdown}
                onCheckedChange={(checked) => {
                  if (checked) {
                    confirmLockdown();
                  } else {
                    void applyControl({ force_lockdown: false });
                  }
                }}
              />
            </div>
          </div>

          <Separator />

          {/* Mensaje al aula */}
          <div className="flex flex-col gap-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="msgInput">Mensaje al aula</Label>
              <Input
                type="text"
                id="msgInput"
                placeholder="Ej: Quedan 10 minutos para entregar"
                value={msgInput}
                onChange={(e) => setMsgInput(e.target.value)}
              />
            </div>
            <div className="flex flex-wrap gap-2">
              <Button onClick={sendMessage}>Enviar mensaje</Button>
              <Button
                variant="outline"
                onClick={() => applyControl({ message: "" })}
              >
                Borrar mensaje
              </Button>
            </div>
          </div>

          {feedback ? (
            <div
              className={cn(
                "flex items-start gap-2 rounded-md border px-3 py-2 text-sm",
                feedback.ok
                  ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400"
                  : "border-destructive/30 bg-destructive/10 text-destructive"
              )}
            >
              {feedback.ok ? (
                <CheckCircle2 className="mt-0.5 size-4 shrink-0" />
              ) : (
                <XCircle className="mt-0.5 size-4 shrink-0" />
              )}
              <span>{feedback.text}</span>
            </div>
          ) : null}
        </CardContent>
      </Card>
    </div>
  );
}

function StatusItem({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  );
}
