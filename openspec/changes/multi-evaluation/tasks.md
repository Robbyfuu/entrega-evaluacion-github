# Tareas: Multi-evaluación (Curso > Sección > Evaluación)

**Change:** multi-evaluation
**Orden:** secuencial. Cada fase deja el sistema funcional y deployable.

---

## Fase 1 — Migraciones SQL

- [ ] T1.1 Crear `csharp/migration-multi-evaluation.sql` con `CREATE TABLE IF NOT EXISTS courses/sections/evaluations` + RLS (`anon_read_*`, `auth_all_*`)
- [ ] T1.2 En el mismo archivo: `ALTER TABLE assignments ADD COLUMN IF NOT EXISTS evaluation_id BIGINT NULL REFERENCES evaluations(id) ON DELETE SET NULL`
- [ ] T1.3 En el mismo archivo: `ALTER TABLE` de `assignment_acceptances`, `student_activity`, `online_clients`, `process_alerts`, `browser_history`, `suspicious_processes` añadiendo `section_id BIGINT NULL REFERENCES sections(id) ON DELETE SET NULL`
- [ ] T1.4 Crear `FUNCTION sync_section_id()` + un trigger `trg_sync_section_*` por cada una de las 6 tablas (BEFORE INSERT OR UPDATE)
- [ ] T1.5 Backfill idempotente: `INSERT INTO courses ('FPY1101','Física I')`, 3 secciones (`001D`,`002D`,`003D`) y 5 evaluaciones por sección (`Evaluación 1..4`,`Examen`) con `ON CONFLICT DO NOTHING`
- [ ] T1.6 Editar `csharp/migration-acceptances.sql`: agregar `p_evaluation_id BIGINT` a la RPC `record_acceptance` y columna `evaluation_id` a `assignment_acceptances`
- [ ] T1.7 Editar `admin-next/migration-realtime.sql`: agregar `courses`, `sections`, `evaluations` al array de tablas publicadas
- [ ] T1.V Verificar: re-correr migración 2× en dev branch sin errores; tras `INSERT` con `section='001D'` el trigger completa `section_id`

## Fase 2 — admin-next types + CRUD

- [ ] T2.1 `admin-next/lib/types.ts`: agregar `CourseRow`, `SectionRow`, `EvaluationRow`; extender `AssignmentRow` con `evaluation_id`; extender `AssignmentAcceptanceRow`, `OnlineClientRow`, `ProcessAlertRow`, `BrowserHistoryRow`, `StudentActivityRow`, `SuspiciousProcess` con `section_id`
- [ ] T2.2 `admin-next/hooks/useCourses.ts` (nuevo): hook CRUD de cursos
- [ ] T2.3 `admin-next/hooks/useEvaluations.ts` (nuevo): hook CRUD de evaluaciones con toggle `active`
- [ ] T2.4 `admin-next/components/sections/CoursesSection.tsx` (nuevo): CRUD de cursos (code, name, active)
- [ ] T2.5 `admin-next/components/sections/EvaluationsSection.tsx` (nuevo): CRUD de evaluaciones por sección + toggle `active`
- [ ] T2.6 `admin-next/components/Sidebar.tsx`: agregar nav items `Cursos` / `Evaluaciones`
- [ ] T2.7 `admin-next/components/sections/Panel.tsx`: montar `CoursesSection` + `EvaluationsSection` en el layout
- [ ] T2.V Verificar: `pnpm build && pnpm lint` pasan

## Fase 3 — admin-next selects dinámicos

- [ ] T3.1 `admin-next/components/sections/AssignmentsSection.tsx`: reemplazar select `asgSection` hardcoded por fetch dinámico; agregar selects Curso/Evaluación al alta; CRUD con `evaluation_id`
- [ ] T3.2 `admin-next/components/sections/SuspiciousProcessesSection.tsx`: reemplazar `const SECTIONS = ["001D","002D","003D"]` por fetch dinámico; agrupar por `section_id`/`section.code`
- [ ] T3.3 `admin-next/components/sections/ActivitySection.tsx`: reemplazar `sectionFilter` hardcoded por fetch dinámico + columna `section_id` con lookup
- [ ] T3.4 `admin-next/components/sections/OnlineClientsSection.tsx`: ajustar badges/columna a `section_id` + lookup
- [ ] T3.5 `admin-next/components/sections/BrowsingSection.tsx`: columna "Sección" → `section_id` + lookup
- [ ] T3.6 `admin-next/components/sections/ProcessAlertsSection.tsx`: columna "Sección" → `section_id` + lookup
- [ ] T3.V Verificar: `pnpm build && pnpm lint` pasan; crear curso+sección+evaluación desde UI y verificar herencia vía `evaluation_id` en `assignments`

## Fase 4 — C# DTOs + SupabaseClient

- [ ] T4.1 `csharp/src/Models/Dtos.cs`: agregar DTOs `Course`, `SectionRow`, `Evaluation` con `[JsonPropertyName("snake_case")]`; extender `Assignment` con `EvaluationId`; extender `Acceptance` con `EvaluationId`
- [ ] T4.2 `csharp/src/Services/SupabaseClient.cs`: agregar `GetCoursesAsync()`, `GetSectionsAsync(courseId)`, `GetEvaluationsAsync(sectionId, onlyActive=true)`
- [ ] T4.3 Ajustar `GetActiveAssignmentsAsync` (filtrar por `evaluation_id`) y `RecordAcceptanceAsync` (nuevo param `evaluation_id`)
- [ ] T4.4 Ajustar `SendHeartbeatAsync`, `ReportProcessAlertAsync`, `ReportStudentActivityAsync`, `ReportBrowsingAsync` para enviar `section_id`
- [ ] T4.5 Ajustar `GetBlocklistAsync` con `COALESCE(section_id, section)`
- [ ] T4.6 `csharp/src/Services/StudentSection.cs`: persistir `SectionId` + `EvaluationId` además de `Section` (TEXT) en HKCU
- [ ] T4.7 `csharp/src/Config.cs`: marcar `Sections` y `EvaluationTypes` como fallback con comentario doc (patrón `SuspiciousProcesses`)
- [ ] T4.V Verificar: `dotnet build` pasa

## Fase 5 — C# UI cascada

- [ ] T5.1 `csharp/src/Windows/MainWindow.xaml`: agregar `CursoCombo` + `EvaluationCombo` en cascada; `TipoCombo` pasa a solo-lectura (muestra `evaluation.title`)
- [ ] T5.2 `csharp/src/Windows/MainWindow.xaml.cs`: `InitAsync` carga cursos → secciones → evaluaciones con fallback `Config.cs` (null=fallo, []=vacio)
- [ ] T5.3 Implementar `SectionCombo_SelectionChanged` + `EvaluationCombo_SelectionChanged` (cascada Curso→Sección→Evaluación)
- [ ] T5.4 Ajustar `FilterBySection`, `ComputeAssignmentStatusesAsync`, `UpdateAssignmentsBanner`, `ShowAssignmentsDialog` para operar sobre la evaluación seleccionada
- [ ] T5.5 `SubirArchivosAsync`: eliminar `switch` `tipoLabel` hardcoded, usar `Evaluation.Title`
- [ ] T5.6 Ajustar `GetRepoName` (slug desde la evaluación seleccionada)
- [ ] T5.7 `csharp/src/Windows/SectionPromptWindow.xaml(.cs)`: extender a selección Curso + Sección + Evaluación
- [ ] T5.V Verificar: `dotnet build` pasa; arrancar cliente con BD caída → combos se llenan desde `Config.cs`

## Fase 6 — Borrado legacy

- [ ] T6.1 Borrar `admin/index.html`
- [ ] T6.2 Borrar `Subir-Evaluacion.ps1`, `Subir-Evaluacion.bat`, `Subir-Evaluacion-DEBUG.bat`
- [ ] T6.3 Borrar `Reset-GitHubAuth.ps1`, `Reset-Internet.bat`
- [ ] T6.4 Verificar con grep que ningún archivo del repo referencia a los borrados
- [ ] T6.V Verificar: `pnpm build && pnpm lint && dotnet build` pasan sin errores

## Fase 7 — Docs

- [ ] T7.1 `README.md`: reescribir sección "Subir tu evaluación" (cascada curso/sección/evaluación desde BD + fallback)
- [ ] T7.2 `docs/Guia-Alumno.tex`: revisar y actualizar referencias a tipos fijos `Evaluacion-1..4`/`Examen`
- [ ] T7.3 `admin-next/README.md`: agregar `csharp/migration-multi-evaluation.sql` al orden de ejecución
- [ ] T7.V Verificar: build de LaTeX pasa (si aplica) + consistencia de enlaces internos

---

## Review Workload Forecast

- Archivos tocados: ~32 (5 nuevos + 21 editados + 6 borrados)
- Líneas estimadas: ~1400-1800
- Chained PRs recomendado: Sí (change cruza 3 capas: SQL → admin-next → C#; cada fase es un slice reviewable)
- 400-line budget risk: Alto

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High
