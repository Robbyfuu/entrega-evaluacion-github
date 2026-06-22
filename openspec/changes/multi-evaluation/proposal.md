# Propuesta de Cambio: Multi-evaluación (Curso > Sección > Evaluación)

**ID:** multi-evaluation
**Estado:** Propuesto
**Autor:** Roberto Arce (FPY1101)
**Fecha:** 2026-06-21

---

## 1. Intención

Eliminar los hardcodeos de `Config.Sections` ("001D","002D","003D") y `Config.EvaluationTypes` ("Evaluacion-1..4","Examen") que viven en ~10 archivos del repo, modelando **curso > sección > evaluación** como datos en Supabase. Habilita múltiples cursos, secciones arbitrarias y evaluaciones dinámicas sin tocar código.

## 2. Alcance

### Incluido
- Tablas nuevas: `courses`, `sections`, `evaluations` (RLS + seeds idempotentes).
- `assignments` con FK `evaluation_id`; backfill desde `Config.EvaluationTypes`.
- `section TEXT` → `section_id BIGINT NULL` (forward-compatible) en 6 tablas + trigger sincronizador.
- C#: combos Curso→Sección→Evaluación en cascada fetcheados de BD, fallback `Config.cs`.
- admin-next: secciones Cursos/Evaluaciones (CRUD) + selects dinámicos.
- **Borrar** `admin/index.html`, `Subir-Evaluacion.*`, `Reset-*` (deprecados).
- Docs: `README.md`, `docs/Guia-Alumno.tex`.

### Excluido
- `control` (id=1) sigue global. Realtime para assignments/evaluations. Tests (change separado).

## 3. Capacidades

### Nuevas
- `course-management`: CRUD de cursos (code, name, active).
- `section-management`: CRUD de secciones por curso.
- `evaluation-management`: CRUD de evaluaciones por sección + activación.

### Modificadas
- `assignment-management`: cada assignment referencia `evaluation_id` (hereda curso/sección).
- `student-onboarding`: alumno elige sección, ve lista de evaluaciones activas de su sección.
- `suspicious-processes`: blocklist usa `section_id` en vez de `section TEXT`.

## 4. Enfoque

Migración forward-compatible (patrón de `migration-blocklist.sql`): columnas nullable + trigger sincroniza. Backfill idempotente convirtiendo `Evaluacion-1..4`/`Examen` a filas de `evaluations`. UI cascada con fallback a `Config.cs` si BD no responde.

## 5. Áreas Afectadas

- `csharp/*.sql`, `admin-next/migration-realtime.sql` — nuevo (tablas + backfill)
- `csharp/src/{Models,Services,Windows}`, `Config.cs` — modificado (cascada + fallback)
- `admin-next/components/sections/*`, `lib/types.ts` — modificado (CRUD + interfaces)
- `admin/index.html`, `Subir-Evaluacion.*`, `Reset-*` — removido
- `README.md`, `docs/*` — modificado

## 6. Riesgos

| Riesgo | Mitigación |
|--------|------------|
| Migración `section`→`section_id` rompe clientes viejos | `section_id` nullable + trigger; `section TEXT` se mantiene |
| Backfill incorrecto de evaluaciones | Idempotente + dry-run en dev branch |
| Fallback `Config.cs` desincronizado | Patrón `SuspiciousProcesses` + doc de sync |

## 7. Rollback

Migraciones son `ADD COLUMN IF NOT EXISTS`/`CREATE TABLE IF NOT EXISTS` (no destructivas). Rollback: `DROP TABLE IF EXISTS evaluations, sections, courses CASCADE` + restaurar archivos borrados desde git. Clientes v2.5.x siguen funcionando contra `section TEXT`.

## 8. Dependencias

- Supabase project `oiownlxyquarmqwauegf` (SQL Editor).
- Release Velopack v2.6.x para distribuir cliente actualizado.

## 9. Criterios de Éxito

- [ ] Profe crea curso + sección + evaluación desde admin-next sin tocar código.
- [ ] Alumno ve solo evaluaciones activas de su sección al arrancar.
- [ ] Clientes v2.5.x siguen reportando vía `section TEXT`.
- [ ] `Config.Sections`/`EvaluationTypes` solo como fallback.
- [ ] `admin/index.html` y `Subir-Evaluacion.ps1` eliminados.
- [ ] `dotnet build` + `pnpm build` + `pnpm lint` pasan.
