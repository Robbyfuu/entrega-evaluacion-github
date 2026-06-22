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

Correr en el SQL Editor de Supabase, **en este orden**. Todas son idempotentes.

1. `../csharp/setup-supabase.sql` — esquema base (una vez, en un proyecto nuevo).
2. `../csharp/migration-acceptances.sql` — aceptación de tareas de Classroom.
3. `../csharp/migration-browser.sql` — historial de navegación del navegador embebido.
4. `migration-realtime.sql` — habilita Realtime en las tablas de monitoreo.
5. `../csharp/migration-blocklist.sql` — blocklist de procesos por sección + RPC de alertas.
6. `../csharp/migration-multi-evaluation.sql` — multi-evaluación (cursos, secciones, evaluaciones + `section_id` forward-compatible).

> ⚠️ La #5 incluye la RPC `report_process_alert`. El paso DIFERIDO que quita el INSERT
> directo de anon sobre `process_alerts` NO se aplica ahí: hacerlo recién cuando el
> cliente C# que usa la RPC esté desplegado en todas las máquinas (ver el comentario de
> secuencia dentro del SQL).

> ⚠️ La #6 re-corre las migraciones #2 y #4 con firmas actualizadas (`record_acceptance`
> con `p_evaluation_id`, realtime con `courses`/`sections`/`evaluations`). Re-corre #2
> y #4 **después** de #6 para asegurar que la última firma de la RPC quede instalada.

## Secciones del panel

Resumen, Controles remotos, **Cursos**, **Secciones**, **Evaluaciones**, PCs conectados, Alertas, Navegación, **Procesos** (blocklist editable por sección), Tareas Classroom, Actividad, Trampas.

Ver `docs/blocklist-procesos.md` para el detalle del blocklist editable.
