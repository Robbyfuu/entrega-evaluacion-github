"use client";

import type { LucideIcon } from "lucide-react";
import {
  Activity,
  AlertTriangle,
  BookOpen,
  ClipboardList,
  Globe,
  LayoutDashboard,
  ListChecks,
  Monitor,
  ScrollText,
  ShieldAlert,
  ShieldX,
  Users,
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

// Same nav items + targets/ids as the original sidebar. Icons added per item.
const NAV_ITEMS: NavItem[] = [
  { target: "sec-kpi", label: "Resumen", icon: LayoutDashboard },
  { target: "sec-control", label: "Controles", icon: ShieldAlert },
  { target: "sec-courses", label: "Cursos", icon: BookOpen },
  { target: "sec-sections", label: "Secciones", icon: ListChecks },
  { target: "sec-evaluations", label: "Evaluaciones", icon: ClipboardList },
  { target: "sec-roster", label: "Roster", icon: Users },
  { target: "sec-pcs", label: "PCs conectados", icon: Monitor },
  { target: "sec-alerts", label: "Alertas", icon: AlertTriangle },
  { target: "sec-browsing", label: "Navegación", icon: Globe },
  { target: "sec-suspicious", label: "Procesos", icon: ShieldX },
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
        <SidebarGroup>
          <SidebarGroupLabel>Navegación</SidebarGroupLabel>
          <SidebarMenu>
            {NAV_ITEMS.map((item) => {
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
      </SidebarContent>
      <SidebarRail />
    </ShadSidebar>
  );
}
