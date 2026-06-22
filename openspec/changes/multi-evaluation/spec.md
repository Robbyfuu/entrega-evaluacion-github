# Especificacion: Multi-evaluacion (Curso > Seccion > Evaluacion)

**Change:** multi-evaluation
**Formato:** Requisitos + Escenarios (Given / When / Then)

---

## REQ-1: course-management (CRUD de cursos)

El sistema MUST proveer una tabla `courses (id BIGSERIAL PK, code TEXT UNIQUE NOT NULL, name TEXT NOT NULL, active BOOL DEFAULT true, created_at TIMESTAMPTZ)` con RLS habilitado.

- `anon_read_courses`: SELECT para anyone (alumno fetchea al elegir).
- `auth_all_courses`: CRUD para profe autenticado.

### Escenario 1.1: Profe crea curso desde admin-next
- **Given** el profe logueado en admin-next
- **When** completa codigo `FPY1101`, nombre "Fisica I" y guarda
- **Then** se inserta una fila en `courses` con `active=true`
- **And** el codigo es unico (viola UNIQUE si repite)

### Escenario 1.2: Alumno fetchea cursos
- **Given** un alumno arrancando el cliente C#
- **When** consulta `courses?active=eq.true&select=*`
- **Then** recibe la lista de cursos activos sin autenticar

---

## REQ-2: section-management (CRUD de secciones)

El sistema MUST proveer `sections (id BIGSERIAL PK, course_id BIGINT NOT NULL REFERENCES courses(id) ON DELETE CASCADE, code TEXT NOT NULL, name TEXT NOT NULL, created_at TIMESTAMPTZ)` con RLS y UNIQUE `(course_id, code)`.

- `anon_read_sections`: SELECT para anyone.
- `auth_all_sections`: CRUD para profe.

### Escenario 2.1: Profe crea seccion bajo un curso
- **Given** existe el curso `FPY1101`
- **When** el profe agrega seccion `001D` ligada a ese curso
- **Then** se inserta la fila con FK valida
- **And** no puede crear otra `001D` para el mismo curso (UNIQUE)

### Escenario 2.2: Cascada on delete
- **Given** un curso con secciones y evaluaciones hijas
- **When** el profe borra el curso
- **Then** secciones y evaluaciones referenciadas se borran en cascada

---

## REQ-3: evaluation-management (CRUD de evaluaciones)

El sistema MUST proveer `evaluations (id BIGSERIAL PK, section_id BIGINT NOT NULL REFERENCES sections(id) ON DELETE CASCADE, title TEXT NOT NULL, classroom_url TEXT, org TEXT, active BOOL DEFAULT true, created_at TIMESTAMPTZ)` con RLS.

- `anon_read_evaluations`: SELECT donde `active=true` (alumno solo ve activas).
- `auth_all_evaluations`: CRUD para profe.

### Escenario 3.1: Profe crea evaluacion para una seccion
- **Given** existe la seccion `001D` bajo `FPY1101`
- **When** el profe crea "Parcial 1" con `classroom_url` y `org` llenos
- **Then** la fila queda con `active=false` por defecto hasta que la active

### Escenario 3.2: Alumno solo ve evaluaciones activas de su seccion
- **Given** existen 3 evaluaciones en `001D`, solo 1 con `active=true`
- **When** el alumno fetchea `evaluations?section_id=eq.X&active=eq.true`
- **Then** recibe exactamente 1 fila
- **And** no ve las inactivas

---

## REQ-4: assignment-management modificado

`assignments` MUST agregar `evaluation_id BIGINT NULL REFERENCES evaluations(id) ON DELETE SET NULL` (nullable para backfill). El curso y seccion se heredan via `evaluations -> sections -> courses`. La RPC `record_acceptance` MUST recibir `p_evaluation_id BIGINT` ademas de los parametros actuales.

### Escenario 4.1: Assignment referencia evaluacion
- **Given** existe la evaluacion "Parcial 1" (id=7)
- **When** el profe crea un assignment con `evaluation_id=7`
- **Then** no necesita setear `section`/`course_id` (se heredan)

### Escenario 4.2: Backfill deja evaluation_id NULL
- **Given** assignments pre-migracion con `section TEXT` lleno
- **When** se corre la migracion
- **Then** `evaluation_id` queda NULL hasta backfill explicito
- **And** los clientes viejos siguen insertando via `section TEXT`

---

## REQ-5: student-onboarding modificado

El cliente C# MUST presentar una cascada Curso -> Seccion -> Evaluacion fetcheada de BD, con fallback a `Config.cs` (`Sections`, `EvaluationTypes`) si la BD no responde (patron null=fallo, []=vacio). El alumno elige seccion (persistida en HKCU) y luego hace click en la evaluacion activa que rendira.

### Escenario 5.1: Cascada feliz
- **Given** el alumno arranca el cliente y la BD responde
- **When** elige curso `FPY1101` y seccion `001D`
- **Then** ve la lista de evaluaciones activas de esa seccion
- **And** al hacer click en una, se persiste su seleccion

### Escenario 5.2: Fallback Config.cs
- **Given** la BD no responde (timeout/red)
- **When** el cliente carga los combos
- **Then** se llenan desde `Config.Sections` y `Config.EvaluationTypes`
- **And** el alumno puede continuar operando

---

## REQ-6: suspicious-processes modificado

`suspicious_processes` MUST agregar `section_id BIGINT NULL REFERENCES sections(id)` manteniendo `section TEXT` para coexistencia. Un trigger sincroniza `section_id` desde `section TEXT` cuando este cambia (patron forward-compatible de `migration-blocklist.sql`). La query del cliente MUST usar `section_id` cuando exista, si no caer a `section TEXT`.

### Escenario 6.1: Trigger sincroniza
- **Given** un INSERT con `section='001D'` y `section_id=NULL`
- **When** se ejecuta el trigger
- **Then** busca el `section_id` correspondiente y lo setea
- **And** si no encuentra match, deja `section_id=NULL`

---

## REQ-7: Migracion forward-compatible section_id

Las tablas `student_activity`, `online_clients`, `process_alerts`, `browser_history`, `assignment_acceptances`, `suspicious_processes` MUST agregar `section_id BIGINT NULL` + trigger sincronizador `section TEXT -> section_id`. Los clientes v2.5.x que reportan via `section TEXT` MUST seguir funcionando sin cambios.

### Escenario 7.1: Cliente viejo reporta via section TEXT
- **Given** un cliente v2.5.x reportando heartbeat con `section='001D'`
- **When** la fila llega a `online_clients`
- **Then** el trigger completa `section_id` con el FK correspondiente
- **And** el cliente no necesita actualizarse

### Escenario 7.2: Re-ejecucion de migracion
- **Given** la migracion ya aplicada
- **When** se corre nuevamente
- **Then** no falla (`ADD COLUMN IF NOT EXISTS`, `DROP POLICY IF EXISTS` antes de `CREATE POLICY`)
- **And** no duplica policies ni triggers

---

## REQ-8: Backfill idempotente desde Config.cs

La migracion MUST insertar filas en `courses`, `sections`, `evaluations` desde los valores hardcodeados de `Config.cs`:
- `Config.Sections` ("001D","002D","003D") -> filas de `sections` bajo un curso default `FPY1101`.
- `Config.EvaluationTypes` ("Evaluacion-1..4","Examen") -> filas de `evaluations` por seccion.

El backfill MUST ser re-corrible (`ON CONFLICT DO NOTHING`).

### Escenario 8.1: Backfill re-corrible
- **Given** la primera corrida del backfill
- **When** se ejecuta por segunda vez
- **Then** no duplica filas (`ON CONFLICT DO NOTHING`)
- **And** no modifica ids existentes

### Escenario 8.2: Curso default creado
- **Given** no existe `courses`
- **When** corre el backfill
- **Then** se crea el curso `FPY1101` y bajo el las 3 secciones + 5 evaluaciones por seccion

---

## REQ-9: Borrado de archivos legacy

Se MUST borrar del repo: `admin/index.html`, `Subir-Evaluacion.ps1`, `Subir-Evaluacion.bat`, `Subir-Evaluacion-DEBUG.bat`, `Reset-GitHubAuth.ps1`, `Reset-Internet.bat`. El build de admin-next (`pnpm build` + `pnpm lint`) y de C# (`dotnet build`) MUST seguir pasando.

### Escenario 9.1: Build sin legacy
- **Given** los archivos legacy borrados
- **When** se ejecuta `pnpm build && pnpm lint && dotnet build`
- **Then** todos pasan sin errores
- **And** ningun archivo del repo referencia a los borrados

---

## REQ-10: control.id=1 intocado + idempotencia

La tabla `control` (single-row `id=1 CHECK (id=1)`) MUST permanecer sin cambios estructurales (sigue siendo global para `internet_block`, `force_lockdown`, `message`). Todas las migraciones del change MUST ser idempotentes (`CREATE TABLE IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`, `DROP POLICY IF EXISTS` antes de `CREATE POLICY`, `ON CONFLICT DO NOTHING`).

### Escenario 10.1: Control sigue global
- **Given** el change aplicado
- **When** el profe togglear `force_lockdown`
- **Then** afecta a todos los alumnos conectados (sin filtro por evaluacion)

### Escenario 10.2: Migracion idempotente re-corrible
- **Given** todas las migraciones del change aplicadas
- **When** se re-corren en orden
- **Then** ninguna falla por objetos ya existentes
