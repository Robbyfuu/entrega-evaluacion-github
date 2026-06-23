"use client";

import { useCallback, useState } from "react";
import type { User } from "@supabase/supabase-js";
import type { OnlineClientRow } from "@/lib/types";
import { useTheme } from "@/hooks/useTheme";
import { useControl } from "@/hooks/useControl";
import { useEvaluationControl } from "@/hooks/useEvaluationControl";
import { Topbar } from "@/components/Topbar";
import { Sidebar } from "@/components/Sidebar";
import { ProcessModal } from "@/components/ProcessModal";
import { KpiRow } from "@/components/sections/KpiRow";
import { ControlSection } from "@/components/sections/ControlSection";
import { CoursesSection } from "@/components/sections/CoursesSection";
import { SectionsSection } from "@/components/sections/SectionsSection";
import { EvaluationsSection } from "@/components/sections/EvaluationsSection";
import { RosterImportSection } from "@/components/sections/RosterImportSection";
import { OnlineClientsSection } from "@/components/sections/OnlineClientsSection";
import { ProcessAlertsSection } from "@/components/sections/ProcessAlertsSection";
import { BrowsingSection } from "@/components/sections/BrowsingSection";
import { SuspiciousProcessesSection } from "@/components/sections/SuspiciousProcessesSection";
import { AssignmentsSection } from "@/components/sections/AssignmentsSection";
import { ActivitySection } from "@/components/sections/ActivitySection";
import { CheatEventsSection } from "@/components/sections/CheatEventsSection";

interface PanelProps {
  user: User;
}

// Authenticated panel shell: topbar + sidebar + all sections.
// KPIs aggregate live counts lifted from the relevant sections.
export function Panel({ user }: PanelProps) {
  const { isDark, toggle } = useTheme();
  const { control, error: controlError, refresh: refreshControl } = useControl();

  // Per-evaluation control scope. null = global control (id=1, affects ALL
  // sections). A non-null id switches the control card to a per-evaluation
  // override and hides the global blast-radius toggle (correction 5).
  const [selectedEvaluationId, setSelectedEvaluationId] = useState<number | null>(null);
  const {
    control: evalControl,
    error: evalControlError,
    setEvaluationControl,
  } = useEvaluationControl(selectedEvaluationId);

  const [onlineCount, setOnlineCount] = useState(0);
  const [alertCount, setAlertCount] = useState(0);
  const [modalClient, setModalClient] = useState<OnlineClientRow | null>(null);
  const [activeNav, setActiveNav] = useState("sec-kpi");

  const handleNav = useCallback((target: string) => {
    setActiveNav(target);
    const el = document.getElementById(target);
    el?.scrollIntoView({ behavior: "smooth", block: "start" });
  }, []);

  return (
    <>
      <Topbar userEmail={user.email ?? ""} isDark={isDark} onToggleTheme={toggle} />
      <div className="shell">
        <Sidebar active={activeNav} onSelect={handleNav} />
        <main className="content">
          <KpiRow control={control} onlineCount={onlineCount} alertCount={alertCount} />
          <ControlSection
            control={control}
            error={controlError}
            onRefresh={refreshControl}
            selectedEvaluationId={selectedEvaluationId}
            onSelectEvaluation={setSelectedEvaluationId}
            evalControl={evalControl}
            evalControlError={evalControlError}
            setEvaluationControl={setEvaluationControl}
          />
          <CoursesSection />
          <SectionsSection />
          <EvaluationsSection />
          <RosterImportSection />
          <OnlineClientsSection
            onOpenProcesses={setModalClient}
            onOnlineCountChange={setOnlineCount}
          />
          <ProcessAlertsSection onCountChange={setAlertCount} />
          <BrowsingSection />
          <SuspiciousProcessesSection />
          <AssignmentsSection />
          <ActivitySection />
          <CheatEventsSection />

          <div className="footer">Realtime · Backend: Supabase · Consola Ops</div>
        </main>
      </div>
      <ProcessModal client={modalClient} onClose={() => setModalClient(null)} />
    </>
  );
}
