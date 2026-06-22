"use client";

import { useMemo } from "react";
import { useSections } from "@/hooks/useSections";
import { useCourses } from "@/hooks/useCourses";
import type { SectionRow, CourseRow } from "@/lib/types";

export interface SectionLookup {
  sections: SectionRow[];
  courses: CourseRow[];
  sectionById: Map<number, SectionRow>;
  courseById: Map<number, CourseRow>;
  sectionCodeById: (id: number | null | undefined) => string | null;
  courseCodeBySectionId: (id: number | null | undefined) => string | null;
}

export function useSectionLookup(): SectionLookup {
  const { rows: sections } = useSections();
  const { rows: courses } = useCourses();

  const sectionById = useMemo(() => {
    const m = new Map<number, SectionRow>();
    for (const s of sections) m.set(s.id, s);
    return m;
  }, [sections]);

  const courseById = useMemo(() => {
    const m = new Map<number, CourseRow>();
    for (const c of courses) m.set(c.id, c);
    return m;
  }, [courses]);

  const sectionCodeById = (id: number | null | undefined) => {
    if (id == null) return null;
    return sectionById.get(id)?.code ?? null;
  };

  const courseCodeBySectionId = (id: number | null | undefined) => {
    if (id == null) return null;
    const sec = sectionById.get(id);
    if (!sec) return null;
    return courseById.get(sec.course_id)?.code ?? null;
  };

  return {
    sections,
    courses,
    sectionById,
    courseById,
    sectionCodeById,
    courseCodeBySectionId,
  };
}
