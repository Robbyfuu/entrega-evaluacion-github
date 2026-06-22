"use client";

import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import type { EvaluationRow } from "@/lib/types";

export function useEvaluations() {
  return useRealtimeTable<EvaluationRow & Record<string, unknown>>({
    table: "evaluations",
    order: { column: "created_at", ascending: false },
    getId: (r) => r.id,
  });
}
