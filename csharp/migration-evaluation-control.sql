-- ============================================================
--  Migracion: Control por evaluacion + atribucion por evento
--  Idempotente. Correr en Supabase SQL Editor.
--
--  Capa aditiva ENCIMA de migration-multi-evaluation.sql
--  (que ya provee courses > sections > evaluations PER-SECTION,
--  el trigger sync_section_id y section_id nullable en 6 tablas).
--
--  ORDEN DE EJECUCION: correr DESPUES de setup-supabase.sql
--  (provee set_updated_at() y el control global id=1) y DESPUES
--  de migration-multi-evaluation.sql (provee la tabla
--  evaluations referenciada por todas las FK de aca).
--
--  Esta migracion agrega, de forma aditiva y forward-compatible:
--    1. evaluations.number + UNIQUE(section_id, number) (handle
--       numerico estable para la resolucion de control). El valor
--       de number lo asigna el panel admin; aca NO se adivina
--       desde el title.
--    2. evaluation_control: override POR EVALUACION sobre el
--       control GLOBAL id=1 (que sigue siendo el fallback).
--    3. evaluation_id nullable en las tablas de evento/identidad
--       que aun no lo tienen (incluido cheat_events, que hoy no
--       tiene ni section ni evaluation).
--
--  NO destructivo: no hace DROP de constraints, no swapea UNIQUE
--  keys (el swap de online_clients es de otra migracion), no
--  modifica RPCs (van aparte) y NO HACE NINGUN BACKFILL (ni de
--  number ni de atribucion de evaluation_id: ambos van aparte).
--  Los clientes legacy que leen el control global id=1 siguen
--  funcionando sin cambios.
-- ============================================================

-- ============================================================
--  1. evaluations: number + UNIQUE(section_id, number)
--  number es el handle numerico estable para joinear el
--  control y resolver la evaluacion desde el cliente. title
--  queda como display. Numeracion POR SECCION (cada seccion
--  numera sus evaluaciones de forma independiente).
-- ============================================================

ALTER TABLE public.evaluations
  ADD COLUMN IF NOT EXISTS number INT;

-- UNIQUE por (section_id, number). Indice unico parcial: solo
-- aplica cuando number NO es NULL, para no chocar con filas
-- legacy que aun no tienen number asignado. La columna nace
-- NULL para todas las filas existentes; el panel admin asigna
-- number explicitamente (nunca se infiere desde el title).
CREATE UNIQUE INDEX IF NOT EXISTS ux_eval_section_number
  ON public.evaluations (section_id, number)
  WHERE number IS NOT NULL;

-- ============================================================
--  2. evaluation_control: override por evaluacion
--  NULL en un campo = heredar del control GLOBAL id=1.
--  El control global (public.control id=1) NO se toca: sigue
--  siendo el fallback para clientes legacy y cuando no hay
--  override para la evaluacion.
-- ============================================================

CREATE TABLE IF NOT EXISTS public.evaluation_control (
  evaluation_id BIGINT PRIMARY KEY
    REFERENCES public.evaluations(id) ON DELETE CASCADE,
  internet_block BOOLEAN,
  force_lockdown BOOLEAN,
  message TEXT,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_by TEXT
);

-- evaluation_id es PK -> ya garantiza UNIQUE (un override por
-- evaluacion). No se agrega UNIQUE redundante.

-- updated_at automatico, reutilizando set_updated_at() de
-- setup-supabase.sql (CREATE OR REPLACE alla; aca solo el
-- trigger, idempotente via DROP IF EXISTS).
DROP TRIGGER IF EXISTS trg_eval_control_updated_at
  ON public.evaluation_control;
CREATE TRIGGER trg_eval_control_updated_at
  BEFORE UPDATE ON public.evaluation_control
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- ============================================================
--  3. evaluation_id nullable en tablas de evento/identidad
--  Aditivo y nullable: sin backfill, sin FK NOT NULL. ON DELETE
--  SET NULL para no perder eventos historicos si se borra una
--  evaluacion. assignment_acceptances y assignments ya tienen
--  evaluation_id (migration-multi-evaluation.sql), pero se
--  re-aplican con IF NOT EXISTS por seguridad (no-op si existen).
-- ============================================================

ALTER TABLE public.process_alerts
  ADD COLUMN IF NOT EXISTS evaluation_id BIGINT NULL
  REFERENCES public.evaluations(id) ON DELETE SET NULL;

ALTER TABLE public.browser_history
  ADD COLUMN IF NOT EXISTS evaluation_id BIGINT NULL
  REFERENCES public.evaluations(id) ON DELETE SET NULL;

ALTER TABLE public.student_activity
  ADD COLUMN IF NOT EXISTS evaluation_id BIGINT NULL
  REFERENCES public.evaluations(id) ON DELETE SET NULL;

-- online_clients gana evaluation_id para agrupar re-rendiciones
-- (correccion 4). El swap del UNIQUE key a COALESCE(evaluation_id,0)
-- es de OTRA migracion (resit-isolation); aca SOLO la columna.
ALTER TABLE public.online_clients
  ADD COLUMN IF NOT EXISTS evaluation_id BIGINT NULL
  REFERENCES public.evaluations(id) ON DELETE SET NULL;

-- cheat_events hoy no tiene ni section ni evaluation. Se agrega
-- evaluation_id nullable para poder atribuir (best-effort, en
-- otra migracion) sin romper inserts anon existentes.
ALTER TABLE public.cheat_events
  ADD COLUMN IF NOT EXISTS evaluation_id BIGINT NULL
  REFERENCES public.evaluations(id) ON DELETE SET NULL;

-- assignments ya tiene evaluation_id desde migration-multi-evaluation.sql;
-- se reafirma idempotente (no-op si ya existe).
ALTER TABLE public.assignments
  ADD COLUMN IF NOT EXISTS evaluation_id BIGINT NULL
  REFERENCES public.evaluations(id) ON DELETE SET NULL;

-- ============================================================
--  4. INDICES sobre las nuevas columnas evaluation_id
--  Soportan los filtros por evaluacion del panel y del resolver
--  por-evaluacion del cliente.
-- ============================================================

CREATE INDEX IF NOT EXISTS idx_process_alerts_eval
  ON public.process_alerts (evaluation_id);
CREATE INDEX IF NOT EXISTS idx_browser_history_eval
  ON public.browser_history (evaluation_id);
CREATE INDEX IF NOT EXISTS idx_student_activity_eval
  ON public.student_activity (evaluation_id);
CREATE INDEX IF NOT EXISTS idx_online_clients_eval
  ON public.online_clients (evaluation_id);
CREATE INDEX IF NOT EXISTS idx_cheat_events_eval
  ON public.cheat_events (evaluation_id);

-- ============================================================
--  5. ROW LEVEL SECURITY para evaluation_control
--  Mismo patron que control id=1: anon LEE (el cliente pollea
--  su override para resolver el lock), authenticated ALL (el
--  panel escribe). Las escrituras sensibles via RPC son de otra
--  migracion (no se agregan RPCs aca).
-- ============================================================

ALTER TABLE public.evaluation_control ENABLE ROW LEVEL SECURITY;

-- evaluation_control: anon lee (cliente pollea su override),
-- authenticated CRUD (panel).
DROP POLICY IF EXISTS "anon_read_evaluation_control"
  ON public.evaluation_control;
CREATE POLICY "anon_read_evaluation_control" ON public.evaluation_control
  FOR SELECT USING (true);
DROP POLICY IF EXISTS "auth_all_evaluation_control"
  ON public.evaluation_control;
CREATE POLICY "auth_all_evaluation_control" ON public.evaluation_control
  FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- ============================================================
--  6. REALTIME
--  Publica evaluation_control para que el cliente reciba en vivo
--  los cambios de override (lock/unlock por evaluacion).
-- ============================================================

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_publication_tables
    WHERE pubname = 'supabase_realtime'
      AND schemaname = 'public'
      AND tablename = 'evaluation_control'
  ) THEN
    EXECUTE 'ALTER PUBLICATION supabase_realtime ADD TABLE public.evaluation_control';
    RAISE NOTICE 'Realtime habilitado para public.evaluation_control';
  ELSE
    RAISE NOTICE 'public.evaluation_control ya estaba en supabase_realtime (sin cambios)';
  END IF;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

-- ============================================================
--  7. VERIFICACION
-- ============================================================

SELECT 'evaluations_con_number' AS tabla, COUNT(*) AS filas
  FROM public.evaluations WHERE number IS NOT NULL
UNION ALL
SELECT 'evaluation_control', COUNT(*) FROM public.evaluation_control
UNION ALL
SELECT 'process_alerts_con_eval', COUNT(*)
  FROM public.process_alerts WHERE evaluation_id IS NOT NULL
UNION ALL
SELECT 'browser_history_con_eval', COUNT(*)
  FROM public.browser_history WHERE evaluation_id IS NOT NULL
UNION ALL
SELECT 'student_activity_con_eval', COUNT(*)
  FROM public.student_activity WHERE evaluation_id IS NOT NULL
UNION ALL
SELECT 'online_clients_con_eval', COUNT(*)
  FROM public.online_clients WHERE evaluation_id IS NOT NULL
UNION ALL
SELECT 'cheat_events_con_eval', COUNT(*)
  FROM public.cheat_events WHERE evaluation_id IS NOT NULL;
