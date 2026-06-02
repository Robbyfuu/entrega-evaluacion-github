"use client";

import { useCallback, useState } from "react";
import type { User } from "@supabase/supabase-js";
import type { OnlineClientRow } from "@/lib/types";
import { useTheme } from "@/hooks/useTheme";
import { useControl } from "@/hooks/useControl";
import { Topbar } from "@/components/Topbar";
import { Sidebar } from "@/components/Sidebar";
import { ProcessModal } from "@/components/ProcessModal";
import { KpiRow } from "@/components/sections/KpiRow";
import { ControlSection } from "@/components/sections/ControlSection";
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
          />
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
