"use client";

import { useEffect, useMemo, useState } from "react";
import { ArrowLeft, RefreshCw } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type {
  EnrollmentStatusRow,
  EvaluationRow,
  OnlineClientRow,
  SuspiciousProcess,
} from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { useEnrollments } from "@/hooks/useEnrollments";
import { useEnrollmentCounts } from "@/hooks/useEnrollmentCounts";
import { useEvaluations } from "@/hooks/useEvaluations";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import {
  buildStudents,
  makeSuspChecker,
  ONLINE_WINDOW_MS,
  type SectionStats,
  type UnifiedStudent,
} from "@/lib/section-workspace";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Badge } from "@/components/ui/Badge";
import { BADGE } from "@/lib/colors";
import { SectionsOverview } from "@/components/sections/SectionsOverview";
import { SectionStudentsTable } from "@/components/sections/SectionStudentsTable";
import { StudentDrawer } from "@/components/sections/StudentDrawer";

const ALL_EVALS = "__all__";

// Vista principal centrada en la seccion: nivel 1 (grid de secciones) ->
// nivel 2 (workspace de la seccion: evaluacion + tabla de alumnos) ->
// nivel 3 (drawer del alumno). Unifica roster + estado vivo + aceptacion/entrega.
export function SectionWorkspace() {
  const { sections, sectionCodeById, courseCodeBySectionId } = useSectionLookup();
  const { status, loading: rosterLoading, error: rosterError, refresh: refreshRoster } =
    useEnrollments();
  const { countForSection, refresh: refreshCounts } = useEnrollmentCounts();
  const { rows: evaluations } = useEvaluations();

  const { rows: clients } = useRealtimeTable<OnlineClientRow & Record<string, unknown>>({
    table: "online_clients",
    order: { column: "last_seen", ascending: false },
    limit: 200,
    getId: (r) => `${r.pc_name}|${r.github_username}|${r.evaluation_id ?? 0}`,
  });

  const { rows: suspiciousRows } = useRealtimeTable<SuspiciousProcess & Record<string, unknown>>({
    table: "suspicious_processes",
    order: { column: "process_name", ascending: true },
    getId: (r) => r.id,
  });

  const [selectedSectionId, setSelectedSectionId] = useState<number | null>(null);
  const [evalFilter, setEvalFilter] = useState<string>(ALL_EVALS);
  const [selectedStudent, setSelectedStudent] = useState<UnifiedStudent | null>(null);

  // Tick de 5s para refrescar la ventana de "online" (heartbeat < 90s).
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 5000);
    return () => clearInterval(id);
  }, []);

  const onlineClients = useMemo(() => {
    const cutoff = now - ONLINE_WINDOW_MS;
    return clients.filter((c) => new Date(c.last_seen).getTime() >= cutoff);
  }, [clients, now]);

  const isSuspiciousFor = useMemo(() => makeSuspChecker(suspiciousRows), [suspiciousRows]);

  // Roster (v_enrollment_status, solo filas de roster) agrupado por seccion.
  const rosterBySection = useMemo(() => {
    const map = new Map<number, EnrollmentStatusRow[]>();
    for (const r of status) {
      if (r.source !== "roster" || r.section_id == null) continue;
      const arr = map.get(r.section_id) ?? [];
      arr.push(r);
      map.set(r.section_id, arr);
    }
    return map;
  }, [status]);

  // Stats por seccion para el nivel 1.
  const statsBySection = useMemo(() => {
    const map = new Map<number, SectionStats>();
    for (const sec of sections) {
      const code = sectionCodeById(sec.id);
      const roster = rosterBySection.get(sec.id) ?? [];
      const secClients = onlineClients.filter((c) => c.section_id === sec.id);
      const suspicious = secClients.filter((c) => {
        const procs = Array.isArray(c.processes) ? c.processes : [];
        return procs.some((p) => isSuspiciousFor(p.name, code));
      }).length;
      map.set(sec.id, {
        online: secClients.length,
        enrolled: countForSection(sec.id),
        accepted: roster.filter((r) => r.accepted).length,
        submitted: roster.filter((r) => r.submitted).length,
        suspicious,
      });
    }
    return map;
  }, [sections, rosterBySection, onlineClients, isSuspiciousFor, sectionCodeById, countForSection]);

  const activeEvalTitle = (sectionId: number): string | null => {
    const ev = evaluations.find((e) => e.section_id === sectionId && e.active);
    return ev?.title ?? null;
  };

  // ----- Nivel 2: seccion seleccionada -----
  const selectedSection = sections.find((s) => s.id === selectedSectionId) ?? null;
  const selectedCode = selectedSectionId != null ? sectionCodeById(selectedSectionId) : null;

  const sectionEvaluations: EvaluationRow[] = useMemo(
    () => evaluations.filter((e) => e.section_id === selectedSectionId),
    [evaluations, selectedSectionId]
  );

  const students = useMemo(() => {
    if (selectedSectionId == null) return [];
    const roster = rosterBySection.get(selectedSectionId) ?? [];
    let secClients = onlineClients.filter((c) => c.section_id === selectedSectionId);
    if (evalFilter !== ALL_EVALS) {
      const evId = Number(evalFilter);
      secClients = secClients.filter((c) => c.evaluation_id === evId);
    }
    return buildStudents({
      rosterStatus: roster,
      onlineClients: secClients,
      isSuspiciousFor,
      sectionCode: selectedCode,
    });
  }, [selectedSectionId, rosterBySection, onlineClients, evalFilter, isSuspiciousFor, selectedCode]);

  function refreshAll() {
    void refreshRoster();
    void refreshCounts();
  }

  // ----- Acciones (mismas que OnlineClientsSection) -----
  async function targetLockdown(pc: string | null, github: string | null) {
    if (!github) {
      window.alert("Este alumno no tiene usuario GitHub registrado.");
      return;
    }
    const reason = window.prompt(
      `Lockdown DIRIGIDO a ${pc ?? "?"} (@${github}).\nMotivo (opcional):`,
      "Trampa detectada por el profesor"
    );
    if (reason === null) return;
    const { error } = await supabase.from("targeted_lockdowns").upsert(
      {
        pc_name: pc,
        github_username: github,
        active: true,
        reason: reason || "Bloqueo del profesor",
        released_at: null,
      },
      { onConflict: "pc_name,github_username" }
    );
    window.alert(error ? "Error: " + error.message : `✓ Lockdown enviado a @${github}. Se aplica en <20s.`);
  }

  async function releaseTargetLockdown(pc: string | null, github: string | null) {
    const { error } = await supabase
      .from("targeted_lockdowns")
      .update({ active: false, released_at: new Date().toISOString() })
      .match({ pc_name: pc, github_username: github });
    window.alert(error ? "Error: " + error.message : `✓ Lockdown liberado para @${github}.`);
  }

  // ----- Render -----
  if (selectedSection == null) {
    return (
      <SectionsOverview
        sections={sections}
        stats={statsBySection}
        courseCodeBySectionId={courseCodeBySectionId}
        activeEvalTitle={activeEvalTitle}
        loading={rosterLoading}
        onSelectSection={(id) => {
          setSelectedSectionId(id);
          setEvalFilter(ALL_EVALS);
        }}
      />
    );
  }

  const onlineCount = students.filter((s) => s.online).length;

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardContent className="flex flex-col gap-4 p-5">
          <div className="flex flex-wrap items-center gap-3">
            <Button variant="outline" size="sm" onClick={() => setSelectedSectionId(null)}>
              <ArrowLeft className="size-4" /> Secciones
            </Button>
            <div className="text-lg font-bold">{selectedSection.code}</div>
            {courseCodeBySectionId(selectedSection.id) ? (
              <span className="text-sm text-muted-foreground">
                {courseCodeBySectionId(selectedSection.id)}
              </span>
            ) : null}
            <Badge solidColor={BADGE.success}>{onlineCount} online</Badge>

            <div className="ml-auto flex items-center gap-2">
              <Select value={evalFilter} onValueChange={setEvalFilter}>
                <SelectTrigger className="w-56">
                  <SelectValue placeholder="Evaluación" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL_EVALS}>Todas las evaluaciones</SelectItem>
                  {sectionEvaluations.map((e) => (
                    <SelectItem key={e.id} value={String(e.id)}>
                      {e.title}
                      {e.active ? " (activa)" : ""}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Button variant="outline" size="sm" onClick={refreshAll}>
                <RefreshCw className="size-4" /> Refrescar
              </Button>
            </div>
          </div>

          <SectionStudentsTable
            students={students}
            loading={rosterLoading}
            error={rosterError}
            onSelectStudent={setSelectedStudent}
          />
        </CardContent>
      </Card>

      <StudentDrawer
        student={selectedStudent}
        sectionCode={selectedCode}
        isSuspiciousFor={isSuspiciousFor}
        onClose={() => setSelectedStudent(null)}
        onTargetLockdown={targetLockdown}
        onReleaseLockdown={releaseTargetLockdown}
      />
    </div>
  );
}
