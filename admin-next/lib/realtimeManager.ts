import type {
  RealtimeChannel,
  RealtimePostgresChangesPayload,
} from "@supabase/supabase-js";
import { supabase } from "@/lib/supabase";

// A consumer-facing realtime listener. Every consumer of a table receives the
// same raw postgres_changes payload; the per-consumer patch logic lives in the
// caller (e.g. useRealtimeTable), not here.
export type RealtimeListener = (
  payload: RealtimePostgresChangesPayload<Record<string, unknown>>
) => void;

interface TableEntry {
  channel: RealtimeChannel;
  listeners: Set<RealtimeListener>;
}

// Module-level singleton registry: at most ONE Supabase channel per distinct
// table, shared by every consumer of that table. Supabase rejects adding a
// second .on("postgres_changes") callback after .subscribe() ("cannot add
// postgres_changes callbacks after subscribe()"), so we register exactly one
// callback per table-channel BEFORE subscribing and fan the payload out to all
// consumers in JavaScript over the listeners Set.
const registry = new Map<string, TableEntry>();

// Subscribe a listener to realtime changes on `table`. Returns an idempotent
// unsubscribe function. The first listener for a table opens the channel; the
// last one to leave closes it.
export function subscribeTable(
  table: string,
  listener: RealtimeListener
): () => void {
  let entry = registry.get(table);

  if (!entry) {
    const listeners = new Set<RealtimeListener>();
    // ONE .on(...) BEFORE .subscribe(). The single callback fans out to every
    // listener registered for this table; we never call .on() again for it.
    const channel = supabase
      .channel(`rt-${table}`)
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table },
        (payload: RealtimePostgresChangesPayload<Record<string, unknown>>) => {
          const current = registry.get(table);
          if (!current) return;
          // Snapshot so a listener that unsubscribes (or subscribes) during
          // dispatch cannot mutate the Set we are iterating.
          for (const l of [...current.listeners]) l(payload);
        }
      )
      .subscribe();
    entry = { channel, listeners };
    registry.set(table, entry);
  }

  entry.listeners.add(listener);

  let unsubscribed = false;
  return () => {
    // Idempotent: React StrictMode double-invoke and Fast Refresh can call the
    // cleanup more than once. Never crash on a repeated unsubscribe.
    if (unsubscribed) return;
    unsubscribed = true;

    const current = registry.get(table);
    if (!current) return;
    current.listeners.delete(listener);
    if (current.listeners.size === 0) {
      void supabase.removeChannel(current.channel);
      registry.delete(table);
    }
  };
}
