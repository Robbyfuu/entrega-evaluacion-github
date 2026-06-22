-- ============================================================
--  Migracion: assignment_acceptances (estado de aceptacion)
--  Idempotente. Correr en Supabase SQL Editor.
-- ============================================================

CREATE TABLE IF NOT EXISTS public.assignment_acceptances (
  id BIGSERIAL PRIMARY KEY,
  github_username TEXT NOT NULL,
  assignment_id BIGINT,
  assignment_title TEXT,
  section TEXT,
  repo_name TEXT,
  repo_url TEXT,
  accepted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(github_username, assignment_id)
);
CREATE INDEX IF NOT EXISTS idx_acceptances_user
  ON public.assignment_acceptances (github_username, accepted_at DESC);

ALTER TABLE public.assignment_acceptances ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "anon_insert_acceptances" ON public.assignment_acceptances;
CREATE POLICY "anon_insert_acceptances" ON public.assignment_acceptances
  FOR INSERT WITH CHECK (true);

DROP POLICY IF EXISTS "anon_read_acceptances" ON public.assignment_acceptances;
CREATE POLICY "anon_read_acceptances" ON public.assignment_acceptances
  FOR SELECT TO anon, authenticated USING (true);

DROP POLICY IF EXISTS "anon_update_acceptances" ON public.assignment_acceptances;
CREATE POLICY "anon_update_acceptances" ON public.assignment_acceptances
  FOR UPDATE USING (true) WITH CHECK (true);

-- RPC upsert (SECURITY DEFINER) para registrar/actualizar aceptacion
-- p_evaluation_id es nullable para coexistir con clientes viejos que
-- no envian evaluation_id (forward-compatible).
CREATE OR REPLACE FUNCTION public.record_acceptance(
  p_github_username TEXT,
  p_assignment_id BIGINT,
  p_assignment_title TEXT DEFAULT NULL,
  p_section TEXT DEFAULT NULL,
  p_repo_name TEXT DEFAULT NULL,
  p_repo_url TEXT DEFAULT NULL,
  p_evaluation_id BIGINT DEFAULT NULL
) RETURNS VOID
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  INSERT INTO public.assignment_acceptances
    (github_username, assignment_id, assignment_title, section, repo_name, repo_url, evaluation_id, accepted_at)
  VALUES
    (p_github_username, p_assignment_id, p_assignment_title, p_section, p_repo_name, p_repo_url, p_evaluation_id, NOW())
  ON CONFLICT (github_username, assignment_id) DO UPDATE
  SET assignment_title = EXCLUDED.assignment_title,
      section          = EXCLUDED.section,
      repo_name        = EXCLUDED.repo_name,
      repo_url         = EXCLUDED.repo_url,
      evaluation_id    = EXCLUDED.evaluation_id,
      accepted_at      = NOW();
END;
$$;
GRANT EXECUTE ON FUNCTION public.record_acceptance(TEXT,BIGINT,TEXT,TEXT,TEXT,TEXT,BIGINT)
  TO anon, authenticated;

SELECT 'assignment_acceptances' AS tabla, COUNT(*) FROM public.assignment_acceptances;
