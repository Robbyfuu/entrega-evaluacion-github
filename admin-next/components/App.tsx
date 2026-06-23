"use client";

import { useEffect, useState } from "react";
import type { Session } from "@supabase/supabase-js";
import { supabase } from "@/lib/supabase";
import { LoginView } from "@/components/LoginView";
import { Panel } from "@/components/sections/Panel";

// Top-level auth gate: shows the login view when there is no session,
// otherwise renders the full panel. Listens for auth state changes.
export function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    supabase.auth.getSession().then(({ data }) => {
      setSession(data.session);
      setReady(true);
    });

    const {
      data: { subscription },
    } = supabase.auth.onAuthStateChange((event, newSession) => {
      // Tear down all realtime channels on logout so subscriptions created by
      // the panel do not leak across the login boundary.
      if (event === "SIGNED_OUT") {
        void supabase.removeAllChannels();
      }
      setSession(newSession);
    });

    return () => subscription.unsubscribe();
  }, []);

  if (!ready) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-muted/30">
        <p className="text-sm text-muted-foreground">Cargando...</p>
      </div>
    );
  }

  return session ? <Panel user={session.user} /> : <LoginView />;
}

export default App;
