"use client";

interface NavItem {
  target: string;
  label: string;
  dot: string;
}

// Same nav items + colored dots as the original sidebar.
const NAV_ITEMS: NavItem[] = [
  { target: "sec-kpi", label: "Resumen", dot: "var(--primary)" },
  { target: "sec-control", label: "Controles", dot: "var(--danger)" },
  { target: "sec-pcs", label: "PCs conectados", dot: "var(--success)" },
  { target: "sec-alerts", label: "Alertas", dot: "var(--warning)" },
  { target: "sec-browsing", label: "Navegación", dot: "var(--info)" },
  { target: "sec-tareas", label: "Tareas Classroom", dot: "var(--info)" },
  { target: "sec-activity", label: "Actividad", dot: "var(--text-faint)" },
  { target: "sec-cheat", label: "Trampas", dot: "var(--danger)" },
];

interface SidebarProps {
  active: string;
  onSelect: (target: string) => void;
}

export function Sidebar({ active, onSelect }: SidebarProps) {
  return (
    <nav className="sidebar">
      {NAV_ITEMS.map((item) => (
        <button
          key={item.target}
          className={`nav-item${active === item.target ? " active" : ""}`}
          onClick={() => onSelect(item.target)}
        >
          <span className="dot" style={{ background: item.dot }} />
          {item.label}
        </button>
      ))}
    </nav>
  );
}
