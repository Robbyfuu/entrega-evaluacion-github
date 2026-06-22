-- ============================================================
--  Migracion: Multi-evaluacion (Curso > Seccion > Evaluacion)
--  Idempotente. Correr en Supabase SQL Editor.
--
--  Crea las tablas courses, sections, evaluations y agrega
--  section_id (nullable, forward-compatible) a 6 tablas
--  existentes. Un trigger sincroniza section_id desde section
--  TEXT para que los clientes v2.5.x sigan funcionando sin
--  cambios. Incluye backfill desde Config.Sections y
--  Config.EvaluationTypes.
-- ============================================================

-- ============================================================
--  1. TABLAS NUEVAS
-- ============================================================

CREATE TABLE IF NOT EXISTS public.courses (
  id BIGSERIAL PRIMARY KEY,
  code TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  active BOOLEAN NOT NULL DEFAULT true,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.sections (
  id BIGSERIAL PRIMARY KEY,
  course_id BIGINT NOT NULL REFERENCES public.courses(id) ON DELETE CASCADE,
  code TEXT NOT NULL,
  name TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(course_id, code)
);

CREATE TABLE IF NOT EXISTS public.evaluations (
  id BIGSERIAL PRIMARY KEY,
  section_id BIGINT NOT NULL REFERENCES public.sections(id) ON DELETE CASCADE,
  title TEXT NOT NULL,
  classroom_url TEXT,
  org TEXT,
  active BOOLEAN NOT NULL DEFAULT false,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_sections_course
  ON public.sections (course_id);
CREATE INDEX IF NOT EXISTS idx_evaluations_section
  ON public.evaluations (section_id);
CREATE INDEX IF NOT EXISTS idx_evaluations_active
  ON public.evaluations (active);

-- ============================================================
--  2. ROW LEVEL SECURITY
-- ============================================================

ALTER TABLE public.courses ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.sections ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.evaluations ENABLE ROW LEVEL SECURITY;

-- courses: anon lee (alumno fetchea al elegir), authenticated CRUD
DROP POLICY IF EXISTS "anon_read_courses" ON public.courses;
CREATE POLICY "anon_read_courses" ON public.courses
  FOR SELECT USING (true);
DROP POLICY IF EXISTS "auth_all_courses" ON public.courses;
CREATE POLICY "auth_all_courses" ON public.courses
  FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- sections: anon lee, authenticated CRUD
DROP POLICY IF EXISTS "anon_read_sections" ON public.sections;
CREATE POLICY "anon_read_sections" ON public.sections
  FOR SELECT USING (true);
DROP POLICY IF EXISTS "auth_all_sections" ON public.sections;
CREATE POLICY "auth_all_sections" ON public.sections
  FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- evaluations: anon lee solo activas, authenticated CRUD
DROP POLICY IF EXISTS "anon_read_evaluations" ON public.evaluations;
CREATE POLICY "anon_read_evaluations" ON public.evaluations
  FOR SELECT USING (active = true);
DROP POLICY IF EXISTS "auth_all_evaluations" ON public.evaluations;
CREATE POLICY "auth_all_evaluations" ON public.evaluations
  FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- ============================================================
--  3. ALTER assignments + 6 tablas con section_id
-- ============================================================

ALTER TABLE public.assignments
  ADD COLUMN IF NOT EXISTS evaluation_id BIGINT NULL
  REFERENCES public.evaluations(id) ON DELETE SET NULL;

ALTER TABLE public.assignment_acceptances
  ADD COLUMN IF NOT EXISTS evaluation_id BIGINT NULL
  REFERENCES public.evaluations(id) ON DELETE SET NULL;

ALTER TABLE public.assignment_acceptances
  ADD COLUMN IF NOT EXISTS section_id BIGINT NULL
  REFERENCES public.sections(id) ON DELETE SET NULL;

ALTER TABLE public.student_activity
  ADD COLUMN IF NOT EXISTS section_id BIGINT NULL
  REFERENCES public.sections(id) ON DELETE SET NULL;

ALTER TABLE public.online_clients
  ADD COLUMN IF NOT EXISTS section_id BIGINT NULL
  REFERENCES public.sections(id) ON DELETE SET NULL;

ALTER TABLE public.process_alerts
  ADD COLUMN IF NOT EXISTS section_id BIGINT NULL
  REFERENCES public.sections(id) ON DELETE SET NULL;

ALTER TABLE public.browser_history
  ADD COLUMN IF NOT EXISTS section_id BIGINT NULL
  REFERENCES public.sections(id) ON DELETE SET NULL;

ALTER TABLE public.suspicious_processes
  ADD COLUMN IF NOT EXISTS section_id BIGINT NULL
  REFERENCES public.sections(id) ON DELETE SET NULL;

-- ============================================================
--  4. FUNCTION sync_section_id + triggers
--  Sincroniza section_id desde section TEXT cuando section_id
--  es NULL. Forward-compatible: clientes viejos que reportan
--  via section TEXT siguen funcionando.
-- ============================================================

CREATE OR REPLACE FUNCTION public.sync_section_id()
RETURNS TRIGGER AS $$
BEGIN
  IF NEW.section_id IS NULL AND NEW.section IS NOT NULL THEN
    SELECT s.id INTO NEW.section_id
    FROM public.sections s
    WHERE s.code = NEW.section
    LIMIT 1;
  END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_sync_section_acceptances
  ON public.assignment_acceptances;
CREATE TRIGGER trg_sync_section_acceptances
  BEFORE INSERT OR UPDATE ON public.assignment_acceptances
  FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();

DROP TRIGGER IF EXISTS trg_sync_section_activity
  ON public.student_activity;
CREATE TRIGGER trg_sync_section_activity
  BEFORE INSERT OR UPDATE ON public.student_activity
  FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();

DROP TRIGGER IF EXISTS trg_sync_section_online
  ON public.online_clients;
CREATE TRIGGER trg_sync_section_online
  BEFORE INSERT OR UPDATE ON public.online_clients
  FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();

DROP TRIGGER IF EXISTS trg_sync_section_alerts
  ON public.process_alerts;
CREATE TRIGGER trg_sync_section_alerts
  BEFORE INSERT OR UPDATE ON public.process_alerts
  FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();

DROP TRIGGER IF EXISTS trg_sync_section_browser
  ON public.browser_history;
CREATE TRIGGER trg_sync_section_browser
  BEFORE INSERT OR UPDATE ON public.browser_history
  FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();

DROP TRIGGER IF EXISTS trg_sync_section_susproc
  ON public.suspicious_processes;
CREATE TRIGGER trg_sync_section_susproc
  BEFORE INSERT OR UPDATE ON public.suspicious_processes
  FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();

-- ============================================================
--  5. BACKFILL idempotente
--  Crea curso default FPY1101 + 3 secciones + 5 evaluaciones
--  por seccion desde Config.Sections y Config.EvaluationTypes.
--  Re-corrible: ON CONFLICT DO NOTHING.
-- ============================================================

INSERT INTO public.courses (code, name, active) VALUES
  ('FPY1101', 'Fisica I', true)
ON CONFLICT (code) DO NOTHING;

-- Secciones bajo FPY1101
INSERT INTO public.sections (course_id, code, name)
SELECT c.id, s.code, s.name
FROM public.courses c
CROSS JOIN (VALUES
  ('001D', 'Seccion 001D'),
  ('002D', 'Seccion 002D'),
  ('003D', 'Seccion 003D')
) AS s(code, name)
WHERE c.code = 'FPY1101'
ON CONFLICT (course_id, code) DO NOTHING;

-- Evaluaciones por seccion (mirror de Config.EvaluationTypes)
INSERT INTO public.evaluations (section_id, title, active)
SELECT s.id, e.title, false
FROM public.sections s
CROSS JOIN (VALUES
  ('Evaluacion-1'),
  ('Evaluacion-2'),
  ('Evaluacion-3'),
  ('Evaluacion-4'),
  ('Examen')
) AS e(title)
WHERE s.code IN ('001D', '002D', '003D')
ON CONFLICT DO NOTHING;

-- ============================================================
--  6. REALTIME
--  Publica courses, sections, evaluations para que el panel
--  las vea en vivo al crear/editar.
-- ============================================================

DO $$
DECLARE
  t text;
  tables text[] := array['courses', 'sections', 'evaluations'];
BEGIN
  FOREACH t IN ARRAY tables LOOP
    IF NOT EXISTS (
      SELECT 1 FROM pg_publication_tables
      WHERE pubname = 'supabase_realtime'
        AND schemaname = 'public'
        AND tablename = t
    ) THEN
      EXECUTE format('ALTER PUBLICATION supabase_realtime ADD TABLE public.%I', t);
      RAISE NOTICE 'Realtime habilitado para public.%', t;
    ELSE
      RAISE NOTICE 'public.% ya estaba en supabase_realtime (sin cambios)', t;
    END IF;
  END LOOP;
END $$;

-- ============================================================
--  7. VERIFICACION
-- ============================================================

SELECT 'courses' AS tabla, COUNT(*) AS filas FROM public.courses
UNION ALL
SELECT 'sections', COUNT(*) FROM public.sections
UNION ALL
SELECT 'evaluations', COUNT(*) FROM public.evaluations
UNION ALL
SELECT 'assignments_con_eval', COUNT(*) FROM public.assignments WHERE evaluation_id IS NOT NULL
UNION ALL
SELECT 'online_con_section_id', COUNT(*) FROM public.online_clients WHERE section_id IS NOT NULL;
