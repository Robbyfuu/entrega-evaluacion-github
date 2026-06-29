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

La lista canónica y ordenada es `../csharp/migrations.order`. El bundle
`../csharp/supabase-all-in-one.sql` es **GENERADO** a partir de ese manifiesto
(`sh csharp/build-all-in-one.sh`) y **NO debe editarse a mano**: para cambiar el
bootstrap se edita la migración o `migrations.order` y se regenera el bundle.

**Base + features** (idempotentes, re-ejecutables):

1. `../csharp/setup-supabase.sql` — esquema base (tablas, RLS, policies, RPCs).
2. `../csharp/migration-blocklist.sql` — blocklist de procesos por sección (owner canónico de `suspicious_processes`: tabla + RLS + seed + realtime) + RPC `report_process_alert` con rate-limit. **Va antes de multi-evaluation** porque esa migración le agrega `section_id` a `suspicious_processes`.
3. `../csharp/migration-browser.sql` — historial del navegador embebido.
4. `../csharp/migration-acceptances.sql` — aceptación de tareas de Classroom.
5. `../csharp/migration-multi-evaluation.sql` — cursos > secciones > evaluaciones + `section_id` forward-compatible.
6. `../csharp/migration-submissions.sql` — `assignment_submissions` + `allows_manual_submission`.
7. `migration-realtime.sql` — habilita Realtime en las tablas de monitoreo.
8. `../csharp/migration-allowed-urls.sql` — allowlist del navegador embebido editable por sección.
9. `../csharp/migration-evaluation-control.sql` — control por evaluación + atribución por evento (`evaluation_id`).
10. `../csharp/migration-version-visibility.sql` — `online_clients.app_version` + `control.update_requested_at`. DROPea el heartbeat de 8 args y crea el de 9; **debe ir antes de migration-heartbeat-identity**.
11. `../csharp/migration-self-lock.sql` — `targeted_lockdowns.source` + RPC `report_self_lock` (trampa local visible y liberable desde el panel).
12. `../csharp/migration-enrollments.sql` — roster importado (PII; RLS authenticated-only + RPC `get_my_enrollment`).
13. `../csharp/migration-enrollments-view.sql` — vista `v_enrollment_status` (cruce roster vs actividad).
14. `../csharp/migration-exam-mode.sql` — `evaluations.exam_mode` (Off/AuditOnly/SoftLock/HardLock) + `policy_json`.
15. `../csharp/migration-exam-pdf.sql` — `evaluations.exam_pdf_path` + bucket privado `exam-pdfs` en Storage.
16. `../csharp/migration-exam-pdf-scope.sql` — parte la lectura del bucket `exam-pdfs` en teacher/student (va **después** de exam-pdf).
17. `../csharp/migration-pc-overrides.sql` — overrides de política por PC.

**FASE 2 — endurecimiento por identidad verificada (JWT)** (correr **AL FINAL**, en este orden):

18. `../csharp/migration-rls-identity-hardening.sql` — guard de identidad-presente en las 4 RPC + INSERT-only de anon en cheat/activity/browser.
19. `../csharp/migration-jwt-identity.sql` — helper `jwt_github_username()` + las 4 RPC y las 3 policies `anon_insert_*` exigen el claim verificado. **Debe ir antes** de heartbeat-identity y targeted-read-scope (ambos usan el helper).
20. `../csharp/migration-heartbeat-identity.sql` — guard JWT en `heartbeat` (9 args).
21. `../csharp/migration-targeted-read-scope.sql` — parte la lectura de `targeted_lockdowns` en teacher/student por identidad.

Migraciones de **datos** opcionales (one-off, solo si corresponde; **NO** son parte del
bootstrap y **no** están en `migrations.order`):
`../csharp/migration-backfill-history.sql`, `../csharp/migration-backfill-github-ep3.sql`,
`../csharp/fix-*.sql`.

> ⚠️ La #2 (blocklist) incluye la RPC `report_process_alert`. El paso DIFERIDO que quita el
> INSERT directo de anon sobre `process_alerts` se aplica recién cuando el cliente C# que usa
> la RPC esté desplegado en todas las máquinas (ver el comentario de secuencia dentro del SQL).

> ⚠️ **FASE 2 es BREAKING**: exige clientes enrolados con JWT (sin clientes <2.7.20 activos,
> ENT-23) y se corre fuera de la ventana de examen. **Si corrés una migración PRE-FASE-2
> standalone (rls-identity-hardening / version-visibility / self-lock / setup-supabase)
> DESPUÉS de FASE 2, REVIERTE el endurecimiento** (resucita firmas viejas / policies abiertas):
> re-corré las 4 migraciones de FASE 2 (#18–#21) al terminar. El bundle generado ya respeta
> este orden (FASE 2 al final), así que correr `supabase-all-in-one.sql` completo es seguro.

## Secciones del panel

**Monitoreo**: Secciones (workspace con drill-down sección → alumnos → detalle),
Bloqueados, Internet bloqueado (offline), Resumen, Controles.

**Gestión / Global**: Cursos, Config. secciones, Evaluaciones y tareas (link de
Classroom editable, modo de evaluación, PDF de enunciado, "ver alumnos"), Roster,
PCs conectados (versión por PC + solicitar actualización), Alertas, Navegación,
Procesos (blocklist editable por sección), URLs permitidas, Actividad, Trampas.
