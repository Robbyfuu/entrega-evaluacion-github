-- ============================================================
--  Migracion: enrollments (roster importado desde Blackboard)
--  Idempotente. Correr en Supabase SQL Editor.
--
--  Capa aditiva ENCIMA de migration-multi-evaluation.sql
--  (que ya provee courses > sections > evaluations PER-SECTION).
--  La FK section_id apunta a public.sections(id).
--
--  ORDEN DE EJECUCION: correr DESPUES de setup-supabase.sql
--  (provee set_updated_at()) y DESPUES de migration-multi-evaluation.sql
--  (provee la tabla sections referenciada por la FK de aca).
--
--  enrollments es la PRIMERA tabla con PII (full_name, email). Por eso
--  su politica de RLS es authenticated-ONLY: NO hay policy anon SELECT.
--  El unico acceso anon es via la RPC get_my_enrollment, que devuelve
--  SOLO campos de confirmacion no-PII (nunca full_name ni email ni
--  blackboard_student_id). El panel docente (authenticated) importa el
--  roster y asigna github manualmente.
--
--  NO destructivo: no hace DROP de tablas/constraints existentes, no
--  toca otras tablas. github_username nace NULL (el docente lo completa
--  en el panel; bb-dl exporta el roster con github en null).
-- ============================================================

-- ============================================================
--  1. TABLA enrollments
--  status NUNCA se degrada en un import (ver RPC import_enrollment):
--  un re-import no puede revivir un estado mas bajo. created_at /
--  updated_at automaticos (updated_at via trigger set_updated_at()).
-- ============================================================

CREATE TABLE IF NOT EXISTS public.enrollments (
  id BIGSERIAL PRIMARY KEY,
  section_id BIGINT NOT NULL
    REFERENCES public.sections(id) ON DELETE CASCADE,
  full_name TEXT NOT NULL,
  email TEXT,
  github_username TEXT,
  blackboard_student_id TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'enrolled',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  -- Clave de conflicto del import: un alumno (por blackboard id) aparece
  -- una sola vez por seccion. Re-import = UPDATE, no fila duplicada.
  UNIQUE(section_id, blackboard_student_id)
);

-- ============================================================
--  2. INDICES
--  - ux_enroll_section_github: UNIQUE parcial sobre lower(github)
--    POR SECCION, solo cuando github NO es NULL (multiples filas con
--    github NULL conviven; dos alumnos de la misma seccion no pueden
--    reclamar el mismo github). El set_enrollment_github deja que el
--    23505 de este indice aflore al caller.
--  - idx_enroll_section_github_lower: acelera el lookup por github
--    (get_my_enrollment hace lower(github_username) = lower(...)).
--  - idx_enroll_section: filtros del panel por seccion.
-- ============================================================

CREATE UNIQUE INDEX IF NOT EXISTS ux_enroll_section_github
  ON public.enrollments (section_id, lower(github_username))
  WHERE github_username IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_enroll_section_github_lower
  ON public.enrollments (lower(github_username));

CREATE INDEX IF NOT EXISTS idx_enroll_section
  ON public.enrollments (section_id);

-- updated_at automatico, reutilizando set_updated_at() de
-- setup-supabase.sql (CREATE OR REPLACE alla; aca solo el trigger,
-- idempotente via DROP IF EXISTS).
DROP TRIGGER IF EXISTS trg_enrollments_updated_at
  ON public.enrollments;
CREATE TRIGGER trg_enrollments_updated_at
  BEFORE UPDATE ON public.enrollments
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- ============================================================
--  3. ROW LEVEL SECURITY (authenticated-ONLY)
--  enrollments es la primera tabla con PII. A DIFERENCIA del resto
--  de las tablas (que tienen policy anon SELECT/INSERT), aca NO hay
--  ninguna policy para anon: el cliente anon NO puede leer ni escribir
--  enrollments directamente. Solo authenticated (panel docente) tiene
--  acceso completo. El cliente anon confirma su inscripcion unicamente
--  via la RPC get_my_enrollment (SECURITY DEFINER, no-PII).
-- ============================================================

ALTER TABLE public.enrollments ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "auth_all_enrollments" ON public.enrollments;
CREATE POLICY "auth_all_enrollments" ON public.enrollments
  FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- (Intencionalmente SIN policy para anon: RLS deniega cualquier
--  SELECT/INSERT/UPDATE/DELETE anon sobre enrollments.)

-- ============================================================
--  4. RPCs
--  Disciplina (igual que record_acceptance / heartbeat):
--    - SECURITY DEFINER + SET search_path = public
--    - exactamente UNA firma por nombre (sin overload -> sin 300 de
--      PostgREST). Params nuevos con DEFAULT NULL.
--    - DROP de la firma previa antes del CREATE (CREATE OR REPLACE con
--      firma distinta crearia un segundo signature).
--    - re-GRANT EXECUTE sobre la firma COMPLETA.
-- ============================================================

-- ------------------------------------------------------------
--  4a. import_enrollment (authenticated: panel docente)
--  Upsert por (section_id, blackboard_student_id). El import NUNCA
--  degrada status: si la fila ya existe con un status, se conserva el
--  existente (COALESCE del entrante NO pisa el de la fila). github del
--  entrante solo se aplica si llega no-NULL (el import de bb-dl manda
--  github NULL; el docente lo asigna aparte con set_enrollment_github).
-- ------------------------------------------------------------
DROP FUNCTION IF EXISTS public.import_enrollment(
  BIGINT, TEXT, TEXT, TEXT, TEXT);

CREATE OR REPLACE FUNCTION public.import_enrollment(
  p_section_id BIGINT,
  p_full_name TEXT,
  p_email TEXT,
  p_blackboard_student_id TEXT,
  p_github_username TEXT DEFAULT NULL
) RETURNS BIGINT
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
DECLARE
  v_id BIGINT;
BEGIN
  INSERT INTO public.enrollments
    (section_id, full_name, email, github_username, blackboard_student_id)
  VALUES
    (p_section_id, p_full_name, p_email, p_github_username, p_blackboard_student_id)
  ON CONFLICT (section_id, blackboard_student_id) DO UPDATE
  SET full_name       = EXCLUDED.full_name,
      email           = EXCLUDED.email,
      -- github solo se actualiza si el import trae uno no-NULL; nunca
      -- borra un github ya asignado por el docente.
      github_username = COALESCE(EXCLUDED.github_username, public.enrollments.github_username)
      -- status NO se incluye: el import preserva el status existente
      -- (no lo degrada ni lo cambia). Filas nuevas usan el DEFAULT
      -- 'enrolled'.
  RETURNING id INTO v_id;

  RETURN v_id;
END;
$$;
GRANT EXECUTE ON FUNCTION public.import_enrollment(BIGINT,TEXT,TEXT,TEXT,TEXT)
  TO authenticated;

-- ------------------------------------------------------------
--  4b. set_enrollment_github (authenticated: panel docente)
--  Asigna/cambia el github de una inscripcion. El UNIQUE parcial
--  ux_enroll_section_github puede lanzar 23505 (github duplicado en la
--  seccion): NO se captura; se deja aflorar al caller para que el panel
--  muestre el conflicto. Pasar NULL limpia el github.
-- ------------------------------------------------------------
DROP FUNCTION IF EXISTS public.set_enrollment_github(
  BIGINT, TEXT);

CREATE OR REPLACE FUNCTION public.set_enrollment_github(
  p_enrollment_id BIGINT,
  p_github_username TEXT
) RETURNS VOID
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  UPDATE public.enrollments
  SET github_username = p_github_username
  WHERE id = p_enrollment_id;
END;
$$;
GRANT EXECUTE ON FUNCTION public.set_enrollment_github(BIGINT,TEXT)
  TO authenticated;

-- ------------------------------------------------------------
--  4c. get_my_enrollment (anon + authenticated: cliente C#)
--  El UNICO acceso del cliente anon a enrollments. SECURITY DEFINER
--  para saltar la RLS authenticated-only, PERO devuelve SOLO campos de
--  confirmacion NO-PII: section_id, status y un boolean found. NUNCA
--  devuelve full_name, email ni blackboard_student_id (no debe poder
--  usarse como oraculo de PII). Match por (section_id, lower(github)).
-- ------------------------------------------------------------
DROP FUNCTION IF EXISTS public.get_my_enrollment(
  TEXT, BIGINT);

CREATE OR REPLACE FUNCTION public.get_my_enrollment(
  p_github_username TEXT,
  p_section_id BIGINT
) RETURNS TABLE (
  section_id BIGINT,
  status TEXT,
  found BOOLEAN
)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  RETURN QUERY
  SELECT e.section_id, e.status, TRUE AS found
  FROM public.enrollments e
  WHERE e.section_id = p_section_id
    AND e.github_username IS NOT NULL
    AND lower(e.github_username) = lower(p_github_username)
  LIMIT 1;

  -- Si no hubo match, devolver una fila de confirmacion negativa sin
  -- exponer PII (found = false). Mantiene la forma de respuesta estable
  -- para el cliente.
  IF NOT FOUND THEN
    RETURN QUERY SELECT p_section_id, NULL::TEXT, FALSE;
  END IF;
END;
$$;
GRANT EXECUTE ON FUNCTION public.get_my_enrollment(TEXT,BIGINT)
  TO anon, authenticated;

-- ============================================================
--  5. VERIFICACION
--  (enrollments NO se publica en realtime: no hay consumidor en vivo.)
-- ============================================================

SELECT 'enrollments' AS tabla, COUNT(*) AS filas FROM public.enrollments
UNION ALL
SELECT 'enrollments_con_github', COUNT(*)
  FROM public.enrollments WHERE github_username IS NOT NULL;
