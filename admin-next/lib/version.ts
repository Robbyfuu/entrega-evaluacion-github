// Comparación de versiones "x.y.z". Lógica pura, sin React — extraída de
// OnlineClientsSection para testearla aislada (lib/version.test.ts).

// Compara versiones "x.y.z": >0 si a>b, <0 si a<b, 0 iguales. Los segmentos no
// numéricos cuentan como 0 (parseInt(...) || 0) y las longitudes distintas se
// rellenan con 0 (un "2.7" equivale a "2.7.0").
export function compareVersions(a: string, b: string): number {
  // Un segmento cuenta solo si es TODO digitos; cualquier otra cosa ("x", "1a",
  // "") -> 0. Estricto a proposito (no como parseInt, que aceptaria prefijos
  // parciales tipo "1a"->1). Las versiones reales son x.y.z numericas.
  const toSegments = (v: string) =>
    v.split(".").map((n) => (/^\d+$/.test(n) ? parseInt(n, 10) : 0));
  const pa = toSegments(a);
  const pb = toSegments(b);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (d !== 0) return d;
  }
  return 0;
}
