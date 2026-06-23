"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { supabase } from "@/lib/supabase";

// Enrolled-student count per section_id, used as the denominator ("X de N")
// for acceptance/submission tallies. Selects only section_id (no PII) and
// counts 'enrolled' rows client-side. enrollments is not realtime, so this
// fetches once; callers may refresh on demand.
export function useEnrollmentCounts() {
  const [counts, setCounts] = useState<Map<number, number>>(new Map());

  const refresh = useCallback(async () => {
    const { data, error } = await supabase
      .from("enrollments")
      .select("section_id, status");
    if (error || !data) return;
    const m = new Map<number, number>();
    for (const row of data as Array<{ section_id: number; status: string }>) {
      if (row.status !== "enrolled") continue;
      m.set(row.section_id, (m.get(row.section_id) ?? 0) + 1);
    }
    setCounts(m);
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const countForSection = useMemo(
    () => (sectionId: number | null | undefined) =>
      sectionId == null ? null : counts.get(sectionId) ?? 0,
    [counts]
  );

  return { counts, countForSection, refresh };
}
