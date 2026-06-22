-- ============================================================
--  Migracion: assignment_submissions (entrega formal de repo)
--  Idempotente. Correr en Supabase SQL Editor.
--
--  Diferencia con assignment_acceptances:
--    - acceptances = el alumno "acepto" la tarea en Classroom
--    - submissions = el alumno "entrego" su repo (URL manual o
--      auto-detectada desde Classroom) como entrega formal.
--  Ambas tablas coexisten; aceptar != entregar.
-- ============================================================

-- Columna opt-in en assignments: si true, el alumno puede pegar una
-- URL de repo manualmente (sin pasar por Classroom).
ALTER TABLE public.assignments
  ADD COLUMN IF NOT EXISTS allows_manual_submission BOOLEAN NOT NULL DEFAULT FALSE;

CREATE TABLE IF NOT EXISTS public.assignment_submissions (
  id BIGSERIAL PRIMARY KEY,
  assignment_id BIGINT NOT NULL,
  github_username TEXT NOT NULL,
  repo_url TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'submitted',
  submitted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(github_username, assignment_id)
);
CREATE INDEX IF NOT EXISTS idx_submissions_assignment
  ON public.assignment_submissions (assignment_id, submitted_at DESC);
CREATE INDEX IF NOT EXISTS idx_submissions_user
  ON public.assignment_submissions (github_username, submitted_at DESC);

ALTER TABLE public.assignment_submissions ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "anon_insert_submissions" ON public.assignment_submissions;
CREATE POLICY "anon_insert_submissions" ON public.assignment_submissions
  FOR INSERT WITH CHECK (true);

DROP POLICY IF EXISTS "anon_read_submissions" ON public.assignment_submissions;
CREATE POLICY "anon_read_submissions" ON public.assignment_submissions
  FOR SELECT TO anon, authenticated USING (true);

DROP POLICY IF EXISTS "anon_update_submissions" ON public.assignment_submissions;
CREATE POLICY "anon_update_submissions" ON public.assignment_submissions
  FOR UPDATE USING (true) WITH CHECK (true);

-- RPC upsert (SECURITY DEFINER) para registrar/actualizar entrega.
DROP FUNCTION IF EXISTS public.record_submission(BIGINT, TEXT, TEXT, TEXT);

CREATE OR REPLACE FUNCTION public.record_submission(
  p_assignment_id BIGINT,
  p_github_username TEXT,
  p_repo_url TEXT,
  p_status TEXT DEFAULT 'submitted'
) RETURNS VOID
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  INSERT INTO public.assignment_submissions
    (assignment_id, github_username, repo_url, status, submitted_at)
  VALUES
    (p_assignment_id, p_github_username, p_repo_url, p_status, NOW())
  ON CONFLICT (github_username, assignment_id) DO UPDATE
  SET repo_url      = EXCLUDED.repo_url,
      status        = EXCLUDED.status,
      submitted_at  = NOW();
END;
$$;
GRANT EXECUTE ON FUNCTION public.record_submission(BIGINT,TEXT,TEXT,TEXT)
  TO anon, authenticated;

SELECT 'assignment_submissions' AS tabla, COUNT(*) FROM public.assignment_submissions;
