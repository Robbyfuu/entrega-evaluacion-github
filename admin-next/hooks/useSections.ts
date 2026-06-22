"use client";

import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import type { SectionRow } from "@/lib/types";

export function useSections() {
  return useRealtimeTable<SectionRow & Record<string, unknown>>({
    table: "sections",
    order: { column: "code", ascending: true },
    getId: (r) => r.id,
  });
}
