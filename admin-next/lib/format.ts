// Date formatting helper, identical to the original panel's `fmt`.
export function fmt(ts: string | null | undefined): string {
  return ts ? new Date(ts).toLocaleString("es-CL") : "";
}

// "hace Ns" / "hace Nmin" relative label for the online clients table.
export function timeAgo(ts: string, now = Date.now()): string {
  const seconds = Math.floor((now - new Date(ts).getTime()) / 1000);
  return seconds < 60 ? `hace ${seconds}s` : `hace ${Math.floor(seconds / 60)}min`;
}
