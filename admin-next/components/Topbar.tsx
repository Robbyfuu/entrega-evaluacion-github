"use client";

import type { ReactElement } from "react";
import { LogOut, Moon, Sun } from "lucide-react";
import { supabase } from "@/lib/supabase";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { SidebarTrigger } from "@/components/ui/sidebar";

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
    <header className="sticky top-0 z-40 flex h-14 items-center gap-2 border-b bg-background/95 px-4 backdrop-blur supports-[backdrop-filter]:bg-background/60">
      <SidebarTrigger className="-ml-1" />
      <Separator orientation="vertical" className="mr-1 h-5" />
      <div className="flex items-center gap-2">
        <h1 className="text-sm font-semibold tracking-tight">Panel del Profesor</h1>
        <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-500/10 px-2.5 py-0.5 text-xs font-medium text-emerald-600 dark:text-emerald-400">
          <span className="size-1.5 animate-pulse rounded-full bg-emerald-500" />
          En vivo
        </span>
      </div>
      <div className="ml-auto flex items-center gap-2">
        <span className="hidden text-sm text-muted-foreground sm:inline">{userEmail}</span>
        <Button
          variant="ghost"
          size="icon"
          onClick={onToggleTheme}
          title="Cambiar tema"
          aria-label="Cambiar tema claro/oscuro"
        >
          {isDark ? <Sun className="size-4" /> : <Moon className="size-4" />}
        </Button>
        <Button variant="outline" size="sm" onClick={handleLogout}>
          <LogOut className="size-4" />
          <span className="hidden sm:inline">Cerrar sesión</span>
        </Button>
      </div>
    </header>
  );
}
