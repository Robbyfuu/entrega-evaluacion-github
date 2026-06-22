"use client";

import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import type { CourseRow } from "@/lib/types";

export function useCourses() {
  return useRealtimeTable<CourseRow & Record<string, unknown>>({
    table: "courses",
    order: { column: "code", ascending: true },
    getId: (r) => r.id,
  });
}
