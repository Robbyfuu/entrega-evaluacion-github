"use client";

import { useCallback, useEffect, useId, useRef, useState } from "react";
import type { RealtimePostgresChangesPayload } from "@supabase/supabase-js";
import { supabase } from "@/lib/supabase";

type Order = { column: string; ascending?: boolean };

export interface UseRealtimeTableOptions<T> {
  table: string;
  order?: Order;
  limit?: number;
  // Stable identity for a row, used to dedupe/replace on realtime events.
  getId: (row: T) => string | number;
  // Optional event hook fired for every realtime payload (e.g. sound/flash cue).
  onInsert?: (row: T) => void;
  // When false, the hook only fetches on demand and skips the subscription.
  realtime?: boolean;
}

interface UseRealtimeTableResult<T> {
  rows: T[];
  loading: boolean;
  error: string | null;
  refresh: () => Promise<void>;
}

// Generic "fetch once + subscribe + patch in place" hook for live tables.
export function useRealtimeTable<T extends Record<string, unknown>>(
  options: UseRealtimeTableOptions<T>
): UseRealtimeTableResult<T> {
  const { table, order, limit, getId, onInsert, realtime = true } = options;

  // ID unico por instancia del hook: los canales de Supabase se indexan por
  // topic, asi que varios consumidores de la misma tabla (p. ej. useCourses
  // montado por useSectionLookup + EvaluationsSection + SectionsSection a la
  // vez) compartirian "rt-{table}" y el segundo .on() correria despues del
  // subscribe() del primero ("cannot add postgres_changes callbacks after
  // subscribe()"). Un topic por instancia evita la colision.
  const instanceId = useId();

  const [rows, setRows] = useState<T[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Keep callbacks/config in refs so the subscription effect can stay stable.
  const getIdRef = useRef(getId);
  getIdRef.current = getId;
  const onInsertRef = useRef(onInsert);
  onInsertRef.current = onInsert;
  const orderRef = useRef(order);
  orderRef.current = order;
  const limitRef = useRef(limit);
  limitRef.current = limit;

  const refresh = useCallback(async () => {
    setLoading(true);
    let query = supabase.from(table).select("*");
    const ord = orderRef.current;
    if (ord) query = query.order(ord.column, { ascending: ord.ascending ?? false });
    if (limitRef.current) query = query.limit(limitRef.current);
    const { data, error: err } = await query;
    if (err) {
      setError(err.message);
      setRows([]);
    } else {
      setError(null);
      setRows((data ?? []) as T[]);
    }
    setLoading(false);
  }, [table]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (!realtime) return;

    const channel = supabase
      .channel(`rt-${table}-${instanceId}`)
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table },
        (payload: RealtimePostgresChangesPayload<T>) => {
          setRows((prev) => {
            const idOf = getIdRef.current;
            const ord = orderRef.current;
            const lim = limitRef.current;

            if (payload.eventType === "DELETE") {
              const oldRow = payload.old as T;
              return prev.filter((r) => idOf(r) !== idOf(oldRow));
            }

            const next = payload.new as T;
            const without = prev.filter((r) => idOf(r) !== idOf(next));
            let merged = [next, ...without];

            // Re-sort to keep the table consistent with the initial query order.
            if (ord) {
              const col = ord.column;
              const asc = ord.ascending ?? false;
              merged = merged.slice().sort((a, b) => {
                const av = a[col] as unknown as string;
                const bv = b[col] as unknown as string;
                if (av === bv) return 0;
                const cmp = av < bv ? -1 : 1;
                return asc ? cmp : -cmp;
              });
            }
            if (lim) merged = merged.slice(0, lim);

            if (payload.eventType === "INSERT") onInsertRef.current?.(next);
            return merged;
          });
        }
      )
      .subscribe();

    return () => {
      void supabase.removeChannel(channel);
    };
    // instanceId es estable por montaje (useId), no se incluye en deps: agregarlo
    // cambiaria el tamano del array entre el modulo viejo y el nuevo en Fast
    // Refresh ("dependency array changed size"). El topic sigue siendo unico.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [table, realtime]);

  return { rows, loading, error, refresh };
}
