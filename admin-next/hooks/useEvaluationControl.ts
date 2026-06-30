"use client";

import { useCallback, useEffect, useState } from "react";
import { supabase } from "@/lib/supabase";
import type { EvaluationControlRow } from "@/lib/types";

// Per-evaluation override row (public.evaluation_control). A NULL field inherits
// the global control id=1. Fetches the override for the selected evaluation and
// patches it live from realtime, scoped to that one evaluation_id.
//
// When evaluationId is null (no per-eval selection), this hook stays idle and
// the panel falls back to the global control path (useControl).
export function useEvaluationControl(evaluationId: number | null) {
  const [control, setControl] = useState<EvaluationControlRow | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    if (evaluationId == null) {
      setControl(null);
      setError(null);
      return;
    }
    // maybeSingle: the override row may not exist yet (first run for this
    // evaluation -> inherit global). Absent row is not an error.
    const { data, error: err } = await supabase
      .from("evaluation_control")
      .select("*")
      .eq("evaluation_id", evaluationId)
      .maybeSingle();
    if (err) {
      setError(err.message);
    } else {
      setError(null);
      setControl((data as EvaluationControlRow | null) ?? null);
    }
  }, [evaluationId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (evaluationId == null) return;
    const channel = supabase
      .channel(`rt-eval-control-${evaluationId}`)
      .on(
        "postgres_changes",
        {
          event: "*",
          schema: "public",
          table: "evaluation_control",
          filter: `evaluation_id=eq.${evaluationId}`,
        },
        (payload) => {
          if (payload.eventType === "DELETE") {
            setControl(null);
          } else {
            setControl(payload.new as EvaluationControlRow);
          }
        }
      )
      .subscribe();
    return () => {
      void supabase.removeChannel(channel);
    };
  }, [evaluationId]);

  // Upsert the override for the selected evaluation. Only the provided fields
  // change; omitted fields keep their current value (NULL = inherit global).
  const setEvaluationControl = useCallback(
    async (
      patch: Partial<
        Pick<EvaluationControlRow, "internet_block" | "force_lockdown" | "copilot_block" | "message">
      >
    ): Promise<{ ok: boolean; error?: string }> => {
      if (evaluationId == null) {
        return { ok: false, error: "No hay evaluación seleccionada." };
      }
      const {
        data: { user },
      } = await supabase.auth.getUser();
      const { error: err } = await supabase
        .from("evaluation_control")
        .upsert(
          {
            evaluation_id: evaluationId,
            ...patch,
            updated_by: user?.email ?? null,
          },
          { onConflict: "evaluation_id" }
        );
      if (err) return { ok: false, error: err.message };
      await refresh();
      return { ok: true };
    },
    [evaluationId, refresh]
  );

  return { control, error, refresh, setEvaluationControl };
}
