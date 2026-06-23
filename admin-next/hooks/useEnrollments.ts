"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { supabase } from "@/lib/supabase";
import type { EnrollmentRow, EnrollmentStatusRow } from "@/lib/types";

// enrollments is RLS authenticated-only and is NOT published to realtime
// (no live consumer), so this hook fetches on demand and refetches after
// writes instead of subscribing. The cross-validation data comes from the
// v_enrollment_status view so the client never joins acceptances/submissions
// against the roster itself.

export interface ImportStudent {
  blackboard_student_id: string;
  full_name: string;
  email: string | null;
  github_username: string | null;
}

export interface ImportSummary {
  inserted: number;
  updated: number;
  githubResolved: number;
  githubNull: number;
  total: number;
}

interface UseEnrollmentsResult {
  enrollments: EnrollmentRow[];
  status: EnrollmentStatusRow[];
  loading: boolean;
  error: string | null;
  refresh: () => Promise<void>;
  // Imports a whole roster into one section via the import_enrollment RPC,
  // one call per student. Resolves the per-student inserted/updated counts by
  // diffing existing blackboard ids before the writes. Throws on the first
  // failing RPC so the caller can surface it (no silent drop).
  importRoster: (
    sectionId: number,
    students: ImportStudent[]
  ) => Promise<ImportSummary>;
  // Assigns/clears a github for an enrollment via set_enrollment_github.
  // Lets the 23505 (duplicate github in section) surface to the caller.
  setGithub: (enrollmentId: number, github: string | null) => Promise<void>;
}

export function useEnrollments(): UseEnrollmentsResult {
  const [enrollments, setEnrollments] = useState<EnrollmentRow[]>([]);
  const [status, setStatus] = useState<EnrollmentStatusRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    const [enrollRes, statusRes] = await Promise.all([
      supabase
        .from("enrollments")
        .select("*")
        .order("section_id", { ascending: true })
        .order("full_name", { ascending: true }),
      supabase.from("v_enrollment_status").select("*"),
    ]);
    if (enrollRes.error) {
      setError(enrollRes.error.message);
      setEnrollments([]);
    } else {
      setError(null);
      setEnrollments((enrollRes.data ?? []) as EnrollmentRow[]);
    }
    // The view is best-effort context; a view error must not blank the roster.
    if (statusRes.error) {
      setStatus([]);
    } else {
      setStatus((statusRes.data ?? []) as EnrollmentStatusRow[]);
    }
    setLoading(false);
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const existingByBbId = useMemo(() => {
    const m = new Map<string, EnrollmentRow>();
    for (const e of enrollments) {
      m.set(`${e.section_id}:${e.blackboard_student_id}`, e);
    }
    return m;
  }, [enrollments]);

  const importRoster = useCallback(
    async (sectionId: number, students: ImportStudent[]): Promise<ImportSummary> => {
      let inserted = 0;
      let updated = 0;
      let githubResolved = 0;
      let githubNull = 0;
      for (const s of students) {
        const key = `${sectionId}:${s.blackboard_student_id}`;
        const isUpdate = existingByBbId.has(key);
        const { error: err } = await supabase.rpc("import_enrollment", {
          p_section_id: sectionId,
          p_full_name: s.full_name,
          p_email: s.email,
          p_blackboard_student_id: s.blackboard_student_id,
          p_github_username: s.github_username,
        });
        if (err) {
          // Hard-fail: never silently drop a student. The loop already
          // committed the rows before this one (the upsert is idempotent, so a
          // re-import is safe), so report the real state instead of implying
          // nothing was written. successful = rows committed before the throw.
          const successful = inserted + updated;
          throw new Error(
            `Se importaron ${successful} de ${students.length} alumnos antes del error en ` +
              `"${s.full_name}" (${s.blackboard_student_id}): ${err.message}. ` +
              `Puedes volver a importar el mismo archivo: el import es idempotente.`
          );
        }
        if (isUpdate) updated += 1;
        else inserted += 1;
        if (s.github_username) githubResolved += 1;
        else githubNull += 1;
      }
      await refresh();
      return {
        inserted,
        updated,
        githubResolved,
        githubNull,
        total: students.length,
      };
    },
    [existingByBbId, refresh]
  );

  const setGithub = useCallback(
    async (enrollmentId: number, github: string | null): Promise<void> => {
      const { error: err } = await supabase.rpc("set_enrollment_github", {
        p_enrollment_id: enrollmentId,
        p_github_username: github,
      });
      if (err) {
        // 23505 from the partial-unique index = github already taken in section.
        if (err.code === "23505" || /duplicate|unique/i.test(err.message)) {
          throw new Error(
            "Ese github ya está asignado a otro alumno en la sección."
          );
        }
        throw new Error(err.message);
      }
      await refresh();
    },
    [refresh]
  );

  return { enrollments, status, loading, error, refresh, importRoster, setGithub };
}
