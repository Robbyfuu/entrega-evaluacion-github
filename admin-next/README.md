# Panel del Profesor — Entrega de Evaluación

Panel web de control y proctoring para el profesor (Next.js 16 + Supabase Realtime).

## Stack

- Next.js 16 (App Router) + React 19 + TypeScript
- Tailwind v4, diseño "Consola Ops" (tema claro/oscuro)
- Supabase (`@supabase/supabase-js`) — Auth + Postgres + Realtime
- Export estático (`output: 'export'`), deploy en Vercel
- pnpm (obligatorio)

## Desarrollo

```bash
cd admin-next
pnpm install
pnpm dev          # Turbopack, http://localhost:3000
```

Login = cuenta docente en Supabase Auth (email + contraseña).

### Variables de entorno

Crear `.env.local` (ver `.env.example`):

```
NEXT_PUBLIC_SUPABASE_URL=https://<tu-proyecto>.supabase.co
NEXT_PUBLIC_SUPABASE_ANON_KEY=<anon-key>
```

La anon key es pública por diseño (protegida por RLS). En Vercel, setear ambas como
variables de entorno del proyecto.

## Build y deploy

```bash
pnpm build        # export estático -> out/
```

Deploy en Vercel: Root Directory = `admin-next`, framework Next.js, las 2 env vars arriba.

## Migraciones de Supabase (orden)

Correr en el SQL Editor de Supabase, **en este orden de dependencia**. Todas son
idempotentes (re-ejecutables sin romper nada).

**Base** (bundleada en `../supabase-all-in-one.sql`, se puede correr de una vez):

1. `../csharp/setup-supabase.sql` — esquema base (tablas, RLS, policies, RPCs, seed).
2. `../csharp/migration-browser.sql` — historial del navegador embebido.
3. `../csharp/migration-acceptances.sql` — aceptación de tareas de Classroom.
4. `../csharp/migration-multi-evaluation.sql` — cursos > secciones > evaluaciones + `section_id` forward-compatible (re-corre #2 y #3 con firmas actualizadas).
5. `../csharp/migration-submissions.sql` — `assignment_submissions` + `allows_manual_submission`.
6. `migration-realtime.sql` — habilita Realtime en las tablas de monitoreo.

**Features posteriores** (correr después de la base, en este orden):

7. `../csharp/migration-blocklist.sql` — blocklist de procesos por sección + RPC `report_process_alert` con rate-limit.
8. `../csharp/migration-allowed-urls.sql` — allowlist del navegador embebido editable por sección.
9. `../csharp/migration-evaluation-control.sql` — control por evaluación + atribución por evento (`evaluation_id`).
10. `../csharp/migration-version-visibility.sql` — `online_clients.app_version` + `control.update_requested_at` (versión por PC + solicitar actualización).
11. `../csharp/migration-self-lock.sql` — `targeted_lockdowns.source` + RPC `report_self_lock` (trampa local visible y liberable desde el panel).
12. `../csharp/migration-enrollments.sql` — roster importado (PII; RLS authenticated-only + RPC `get_my_enrollment`).
13. `../csharp/migration-enrollments-view.sql` — vista `v_enrollment_status` (cruce roster vs actividad).
14. `../csharp/migration-exam-mode.sql` — `evaluations.exam_mode` (Off/AuditOnly/SoftLock/HardLock) + `policy_json`.
15. `../csharp/migration-exam-pdf.sql` — `evaluations.exam_pdf_path` + bucket privado `exam-pdfs` en Storage.

Migraciones de **datos** opcionales (one-off, solo si corresponde):
`../csharp/migration-backfill-history.sql`, `../csharp/migration-backfill-github-ep3.sql`.

> ⚠️ La #7 incluye la RPC `report_process_alert`. El paso DIFERIDO que quita el INSERT
> directo de anon sobre `process_alerts` se aplica recién cuando el cliente C# que usa la
> RPC esté desplegado en todas las máquinas (ver el comentario de secuencia dentro del SQL).

> ⚠️ La #4 re-corre #2 y #3 con firmas actualizadas. Si corrés la base por separado (no el
> all-in-one), re-corré #2 y #3 **después** de #4 para asegurar que la última firma de la RPC
> quede instalada.

## Secciones del panel

**Monitoreo**: Secciones (workspace con drill-down sección → alumnos → detalle),
Bloqueados, Internet bloqueado (offline), Resumen, Controles.

**Gestión / Global**: Cursos, Config. secciones, Evaluaciones y tareas (link de
Classroom editable, modo de evaluación, PDF de enunciado, "ver alumnos"), Roster,
PCs conectados (versión por PC + solicitar actualización), Alertas, Navegación,
Procesos (blocklist editable por sección), URLs permitidas, Actividad, Trampas.
