"use client";

import type { ReactElement } from "react";
import { supabase } from "@/lib/supabase";

export interface TopbarProps {
  userEmail: string;
  isDark: boolean;
  onToggleTheme: () => void;
}

export function Topbar(props: TopbarProps): ReactElement {
  const { userEmail, isDark, onToggleTheme } = props;

  async function handleLogout() {
    await supabase.auth.signOut();
    // onAuthStateChange in App handles returning to the login view.
  }

  return (
    <div className="topbar">
      <div className="topbar-brand">
        <span className="logo">D</span>
        <span>Panel Docente</span>
        <span className="pill pill-live">● EN VIVO</span>
      </div>
      <div className="topbar-right">
        <button
          className="theme-toggle"
          onClick={onToggleTheme}
          title="Cambiar tema"
          aria-label="Cambiar tema claro/oscuro"
        >
          {isDark ? "☀" : "◐"}
        </button>
        <span style={{ color: "var(--text-muted)", fontSize: 13 }}>{userEmail}</span>
        <button className="btn-secondary" onClick={handleLogout}>
          Cerrar sesión
        </button>
      </div>
    </div>
  );
}
