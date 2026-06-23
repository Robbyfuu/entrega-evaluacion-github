"use client";

import { useMemo, useState } from "react";
import { CheckCircle2, Globe, RefreshCw, Trash2, XCircle } from "lucide-react";
import { supabase } from "@/lib/supabase";
import type { AllowedUrlRow } from "@/lib/types";
import { useRealtimeTable } from "@/hooks/useRealtimeTable";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { fmt } from "@/lib/format";
import { BADGE } from "@/lib/colors";
import { Badge } from "@/components/ui/Badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

const GLOBAL_LABEL = "Global (todas las secciones)";
const GLOBAL_VALUE = "__global__";

// Dominios demasiado amplios: permitirlos abriria un escape (Search/Docs/etc.)
// durante el examen. Se rechazan en el alta.
const DANGEROUS_DOMAINS = new Set([
  "com", "net", "org", "cl", "io", "co", "app", "dev",
  "google.com", "www.google.com",
  "microsoft.com", "www.microsoft.com",
  "live.com", "office.com", "bing.com", "yahoo.com",
  "facebook.com", "instagram.com", "twitter.com", "x.com",
  "youtube.com", "reddit.com", "wikipedia.org",
]);

type Kind = AllowedUrlRow["kind"];

// Normaliza y valida un patron segun su tipo. Devuelve {value} si es valido o
// {error} con el motivo. SEGURIDAD: bloquea dominios amplios y exige que las
// url exactas tengan ruta (sino el prefijo abriria todo el host).
function validatePattern(raw: string, kind: Kind): { value?: string; error?: string } {
  const t = raw.trim().toLowerCase();
  if (!t) return { error: "Escribe el patrón." };

  if (kind === "exact_url") {
    if (!/^https?:\/\//.test(t)) {
      return { error: "La URL exacta debe empezar con http:// o https://" };
    }
    let u: URL;
    try {
      u = new URL(t);
    } catch {
      return { error: "URL inválida." };
    }
    if (u.pathname.length <= 1) {
      return {
        error:
          "La URL exacta debe incluir una ruta (ej: …/a/duocuc.cl/acs). Para abrir un host completo usa el tipo Dominio.",
      };
    }
    // scheme://host/path sin query ni fragment (igual que el cliente C#).
    return { value: `${u.protocol}//${u.host}${u.pathname}` };
  }

  // kind === "domain": tomar solo el host si pegaron una URL completa.
  let host = t;
  if (/^https?:\/\//.test(t)) {
    try {
      host = new URL(t).host;
    } catch {
      return { error: "Dominio inválido." };
    }
  }
  host = host.replace(/\/.*$/, "").replace(/\.$/, "");
  if (!host.includes(".")) {
    return { error: "Dominio inválido (debe tener al menos un punto, ej: accounts.google.com)." };
  }
  if (DANGEROUS_DOMAINS.has(host)) {
    return {
      error: `"${host}" es demasiado amplio: abriría todo el sitio (escape de examen). Usa un subdominio específico (ej: accounts.google.com) o una URL exacta.`,
    };
  }
  return { value: host };
}

// Allowlist del navegador embebido: editor CRUD sobre la tabla `allowed_urls`.
// El cliente C# la lee en tiempo real (con fallback al hardcode de Config),
// asi el profesor agrega/quita URLs permitidas sin recompilar el cliente.
export function AllowedUrlsSection() {
  const { rows, loading, error, refresh } = useRealtimeTable<
    AllowedUrlRow & Record<string, unknown>
  >({
    table: "allowed_urls",
    order: { column: "pattern", ascending: true },
    getId: (r) => r.id,
  });

  const { sections } = useSectionLookup();

  const [feedback, setFeedback] = useState<{ text: string; ok: boolean } | null>(null);
  const [pattern, setPattern] = useState("");
  const [kind, setKind] = useState<Kind>("domain");
  const [section, setSection] = useState<string>("");

  const sectionCodes = useMemo(() => {
    const codes = new Set<string>();
    for (const s of sections) codes.add(s.code);
    for (const r of rows) if (r.section) codes.add(r.section);
    return Array.from(codes).sort();
  }, [sections, rows]);

  const groups = useMemo(() => {
    const global = rows.filter((r) => r.section === null);
    const bySection = sectionCodes.map((sec) => ({
      key: sec,
      label: sec,
      items: rows.filter((r) => r.section === sec),
    }));
    return [{ key: "__global__", label: GLOBAL_LABEL, items: global }, ...bySection];
  }, [rows, sectionCodes]);

  async function addUrl() {
    const { value, error: vErr } = validatePattern(pattern, kind);
    if (vErr || !value) {
      setFeedback({ text: vErr ?? "Patrón inválido.", ok: false });
      return;
    }
    if (kind === "domain") {
      const ok = window.confirm(
        `Vas a permitir "${value}" y TODOS sus subdominios durante el examen. ¿Continuar?`
      );
      if (!ok) return;
    }
    // .select() para confirmar el write: si la sesión expiró, RLS lo rechaza
    // sin lanzar error (data vacía). Mismo patrón que la blocklist.
    const { data, error: err } = await supabase
      .from("allowed_urls")
      .insert({
        pattern: value,
        kind,
        section: section === "" ? null : section,
      })
      .select();
    if (err) {
      const duplicate = err.code === "23505" || /duplicate|unique/i.test(err.message);
      setFeedback({
        text: duplicate
          ? `"${value}" ya está permitido para esa sección.`
          : "Error: " + err.message,
        ok: false,
      });
      return;
    }
    if (!data || data.length === 0) {
      setFeedback({
        text: "No se pudo agregar (¿sesión expirada?). Vuelve a iniciar sesión.",
        ok: false,
      });
      return;
    }
    setPattern("");
    setFeedback({ text: `"${value}" agregado a las URLs permitidas.`, ok: true });
    void refresh();
  }

  async function deleteUrl(id: AllowedUrlRow["id"]) {
    if (!window.confirm("¿Quitar esta URL de las permitidas?")) return;
    const { data, error: err } = await supabase
      .from("allowed_urls")
      .delete()
      .eq("id", id)
      .select();
    if (err) {
      setFeedback({ text: "Error: " + err.message, ok: false });
      return;
    }
    if (!data || data.length === 0) {
      setFeedback({
        text: "No se pudo eliminar (¿sesión expirada?). Vuelve a iniciar sesión.",
        ok: false,
      });
      return;
    }
    void refresh();
  }

  const total = rows.length;

  return (
    <Card id="sec-allowed" className="mb-4 scroll-mt-20">
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2">
          URLs permitidas
          <span className="text-xs font-normal text-muted-foreground">
            (navegador del alumno durante el bloqueo)
          </span>
          <Badge variant="neutral">{total}</Badge>
        </CardTitle>
        <CardDescription>
          Sitios a los que el navegador embebido puede entrar durante el examen
          (login, GitHub, etc.). Todo lo demás dispara la trampa. Tipo{" "}
          <strong className="font-semibold text-foreground">Dominio</strong>{" "}
          permite el host y sus subdominios; tipo{" "}
          <strong className="font-semibold text-foreground">URL exacta</strong>{" "}
          permite solo esa ruta. Los equipos lo aplican en tiempo real (si la
          tabla falla, el cliente cae a la lista de seguridad por defecto).
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        {/* Alta */}
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <div className="flex w-full flex-col gap-1.5 sm:w-44">
            <Label htmlFor="allowKind">Tipo</Label>
            <Select value={kind} onValueChange={(v) => setKind(v as Kind)}>
              <SelectTrigger id="allowKind" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="domain">Dominio (host + subdominios)</SelectItem>
                <SelectItem value="exact_url">URL exacta (una ruta)</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-1 flex-col gap-1.5">
            <Label htmlFor="allowPattern">
              {kind === "domain" ? "Dominio" : "URL exacta"}
            </Label>
            <Input
              type="text"
              id="allowPattern"
              placeholder={
                kind === "domain"
                  ? "ej: accounts.google.com"
                  : "ej: https://www.google.com/a/duocuc.cl/acs"
              }
              value={pattern}
              onChange={(e) => setPattern(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") void addUrl();
              }}
            />
          </div>
          <div className="flex w-full flex-col gap-1.5 sm:w-56">
            <Label htmlFor="allowSection">Sección</Label>
            <Select
              value={section === "" ? GLOBAL_VALUE : section}
              onValueChange={(value) =>
                setSection(value === GLOBAL_VALUE ? "" : value)
              }
            >
              <SelectTrigger id="allowSection" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={GLOBAL_VALUE}>{GLOBAL_LABEL}</SelectItem>
                {sectionCodes.map((sec) => (
                  <SelectItem key={sec} value={sec}>
                    {sec}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button onClick={addUrl}>Permitir</Button>
        </div>

        {feedback ? (
          <div
            className={cn(
              "flex items-start gap-2 rounded-md border px-3 py-2 text-sm",
              feedback.ok
                ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400"
                : "border-destructive/30 bg-destructive/10 text-destructive"
            )}
          >
            {feedback.ok ? (
              <CheckCircle2 className="mt-0.5 size-4 shrink-0" />
            ) : (
              <XCircle className="mt-0.5 size-4 shrink-0" />
            )}
            <span>{feedback.text}</span>
          </div>
        ) : null}

        {loading && rows.length === 0 ? (
          <div className="rounded-lg border">
            <div className="flex flex-col gap-2 p-3">
              {Array.from({ length: 4 }).map((_, i) => (
                <div key={`sk-${i}`} className="flex items-center justify-between gap-3">
                  <Skeleton className="h-4 w-56" />
                  <Skeleton className="h-4 w-24" />
                  <Skeleton className="size-8 rounded-md" />
                </div>
              ))}
            </div>
          </div>
        ) : error ? (
          <p className="text-sm text-destructive">Error: {error}</p>
        ) : (
          <div className="flex flex-col gap-6">
            {groups.map((group) => (
              <div key={group.key} className="flex flex-col gap-2">
                <div className="flex items-center gap-2">
                  <Badge
                    solidColor={
                      group.key === "__global__" ? BADGE.sectionAlt : BADGE.user
                    }
                  >
                    {group.label}
                  </Badge>
                  <span className="text-xs text-muted-foreground tabular-nums">
                    {group.items.length}
                  </span>
                </div>
                {group.items.length === 0 ? (
                  <div className="flex items-center gap-2 rounded-lg border border-dashed px-3 py-4 text-sm text-muted-foreground">
                    <Globe className="size-4 text-muted-foreground/50" />
                    Sin URLs permitidas en este grupo.
                  </div>
                ) : (
                  <div className="rounded-lg border">
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead className="w-[18%] text-xs font-medium uppercase tracking-wide text-muted-foreground">
                            Tipo
                          </TableHead>
                          <TableHead className="w-[47%] text-xs font-medium uppercase tracking-wide text-muted-foreground">
                            Patrón
                          </TableHead>
                          <TableHead className="w-[20%] text-xs font-medium uppercase tracking-wide text-muted-foreground">
                            Agregado
                          </TableHead>
                          <TableHead className="w-[15%] text-right text-xs font-medium uppercase tracking-wide text-muted-foreground">
                            Acciones
                          </TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {group.items.map((u) => (
                          <TableRow key={u.id}>
                            <TableCell>
                              <Badge variant="neutral">
                                {u.kind === "exact_url" ? "URL exacta" : "Dominio"}
                              </Badge>
                            </TableCell>
                            <TableCell className="font-mono break-all">
                              {u.pattern}
                            </TableCell>
                            <TableCell className="text-xs text-muted-foreground tabular-nums">
                              {fmt(u.created_at)}
                            </TableCell>
                            <TableCell className="text-right">
                              <Button
                                variant="ghost"
                                size="icon-sm"
                                className="text-destructive hover:bg-destructive/10 hover:text-destructive"
                                onClick={() => deleteUrl(u.id)}
                                aria-label="Quitar URL"
                              >
                                <Trash2 className="size-4" />
                              </Button>
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}

        <div>
          <Button variant="outline" size="sm" onClick={refresh}>
            <RefreshCw className="size-4" />
            Refrescar
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
