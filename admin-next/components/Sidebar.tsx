"use client";

import type { LucideIcon } from "lucide-react";
import {
  Activity,
  AlertTriangle,
  BookOpen,
  ClipboardList,
  Globe,
  LayoutDashboard,
  LayoutGrid,
  ListChecks,
  LockKeyhole,
  Monitor,
  ScrollText,
  ShieldAlert,
  ShieldCheck,
  ShieldX,
  Users,
  WifiOff,
} from "lucide-react";
import {
  Sidebar as ShadSidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarRail,
} from "@/components/ui/sidebar";

interface NavItem {
  target: string;
  label: string;
  icon: LucideIcon;
}

// Monitoreo (primario): el workspace por seccion drill-down + resumen global.
const NAV_PRIMARY: NavItem[] = [
  { target: "sec-workspace", label: "Secciones", icon: LayoutGrid },
  { target: "sec-locked", label: "Bloqueados", icon: LockKeyhole },
  { target: "sec-blocked-offline", label: "Internet bloqueado (offline)", icon: WifiOff },
  { target: "sec-kpi", label: "Resumen", icon: LayoutDashboard },
  { target: "sec-control", label: "Controles", icon: ShieldAlert },
];

// Vistas globales / config (cruzan todas las secciones).
const NAV_GLOBAL: NavItem[] = [
  { target: "sec-courses", label: "Cursos", icon: BookOpen },
  { target: "sec-sections", label: "Config. secciones", icon: ListChecks },
  { target: "sec-evaluations", label: "Evaluaciones", icon: ClipboardList },
  { target: "sec-roster", label: "Roster", icon: Users },
  { target: "sec-pcs", label: "PCs conectados", icon: Monitor },
  { target: "sec-alerts", label: "Alertas", icon: AlertTriangle },
  { target: "sec-browsing", label: "Navegación", icon: Globe },
  { target: "sec-suspicious", label: "Procesos", icon: ShieldX },
  { target: "sec-allowed", label: "URLs permitidas", icon: ShieldCheck },
  { target: "sec-tareas", label: "Tareas Classroom", icon: ScrollText },
  { target: "sec-activity", label: "Actividad", icon: Activity },
  { target: "sec-cheat", label: "Trampas", icon: ShieldX },
];

interface SidebarProps {
  active: string;
  onSelect: (target: string) => void;
}

export function Sidebar({ active, onSelect }: SidebarProps) {
  return (
    <ShadSidebar collapsible="offcanvas">
      <SidebarHeader className="px-3 py-4">
        <div className="flex items-center gap-2.5">
          <div className="flex size-8 shrink-0 items-center justify-center rounded-md bg-primary font-bold text-primary-foreground">
            D
          </div>
          <div className="flex flex-col leading-tight group-data-[collapsible=icon]:hidden">
            <span className="text-sm font-semibold">Panel Docente</span>
            <span className="text-xs text-muted-foreground">Entrega Evaluación</span>
          </div>
        </div>
      </SidebarHeader>
      <SidebarContent>
        {([
          { label: "Monitoreo", items: NAV_PRIMARY },
          { label: "Gestión / Global", items: NAV_GLOBAL },
        ] as const).map((group) => (
          <SidebarGroup key={group.label}>
            <SidebarGroupLabel>{group.label}</SidebarGroupLabel>
            <SidebarMenu>
              {group.items.map((item) => {
                const Icon = item.icon;
                return (
                  <SidebarMenuItem key={item.target}>
                    <SidebarMenuButton
                      isActive={active === item.target}
                      tooltip={item.label}
                      onClick={() => onSelect(item.target)}
                    >
                      <Icon />
                      <span>{item.label}</span>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                );
              })}
            </SidebarMenu>
          </SidebarGroup>
        ))}
      </SidebarContent>
      <SidebarRail />
    </ShadSidebar>
  );
}
