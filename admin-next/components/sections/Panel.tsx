"use client";

import { useCallback, useState } from "react";
import type { User } from "@supabase/supabase-js";
import type { OnlineClientRow } from "@/lib/types";
import { useTheme } from "@/hooks/useTheme";
import { useControl } from "@/hooks/useControl";
import { useEvaluationControl } from "@/hooks/useEvaluationControl";
import { SidebarInset, SidebarProvider } from "@/components/ui/sidebar";
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

  // El sidebar cambia de VISTA (dashboard: una seccion a la vez), no hace scroll.
  const handleNav = useCallback((target: string) => {
    setActiveNav(target);
  }, []);

  // Todas las secciones quedan MONTADAS para que el realtime y los contadores
  // de los KPIs sigan vivos; solo se muestra la activa (las demas: display:none).
  const view = (target: string) =>
    activeNav === target ? "block" : "hidden";

  return (
    <SidebarProvider>
      <Sidebar active={activeNav} onSelect={handleNav} />
      <SidebarInset>
        <Topbar userEmail={user.email ?? ""} isDark={isDark} onToggleTheme={toggle} />
        <main className="w-full flex-1 px-4 py-6 sm:px-6 lg:px-8">
          <div className={view("sec-kpi")}>
            <KpiRow control={control} onlineCount={onlineCount} alertCount={alertCount} />
          </div>
          <div className={view("sec-control")}>
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
          </div>
          <div className={view("sec-courses")}><CoursesSection /></div>
          <div className={view("sec-sections")}><SectionsSection /></div>
          <div className={view("sec-evaluations")}><EvaluationsSection /></div>
          <div className={view("sec-roster")}><RosterImportSection /></div>
          <div className={view("sec-pcs")}>
            <OnlineClientsSection
              onOpenProcesses={setModalClient}
              onOnlineCountChange={setOnlineCount}
            />
          </div>
          <div className={view("sec-alerts")}>
            <ProcessAlertsSection onCountChange={setAlertCount} />
          </div>
          <div className={view("sec-browsing")}><BrowsingSection /></div>
          <div className={view("sec-suspicious")}><SuspiciousProcessesSection /></div>
          <div className={view("sec-tareas")}><AssignmentsSection /></div>
          <div className={view("sec-activity")}><ActivitySection /></div>
          <div className={view("sec-cheat")}><CheatEventsSection /></div>
        </main>
      </SidebarInset>
      <ProcessModal client={modalClient} onClose={() => setModalClient(null)} />
    </SidebarProvider>
  );
}
