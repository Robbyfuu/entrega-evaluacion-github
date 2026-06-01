"use client";

import { useCallback, useEffect, useState } from "react";
import { supabase } from "@/lib/supabase";
import type { ControlRow } from "@/lib/types";

// Live state of the single control row (id=1): internet/lockdown/message.
// Fetches on mount, then patches instantly from realtime postgres_changes.
export function useControl() {
  const [control, setControl] = useState<ControlRow | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    const { data, error: err } = await supabase
      .from("control")
      .select("*")
      .eq("id", 1)
      .single();
    if (err) {
      setError(err.message);
    } else {
      setError(null);
      setControl(data as ControlRow);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    const channel = supabase
      .channel("rt-control")
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table: "control", filter: "id=eq.1" },
        (payload) => {
          if (payload.eventType !== "DELETE") {
            setControl(payload.new as ControlRow);
          }
        }
      )
      .subscribe();
    return () => {
      void supabase.removeChannel(channel);
    };
  }, []);

  return { control, error, refresh };
}
