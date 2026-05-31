-- ============================================================
--  Migracion: browser_history (navegacion en WebView del alumno)
--  Idempotente. Correr en Supabase SQL Editor.
-- ============================================================

CREATE TABLE IF NOT EXISTS public.browser_history (
  id BIGSERIAL PRIMARY KEY,
  github_username TEXT,
  pc_name TEXT,
  section TEXT,
  url TEXT NOT NULL,
  domain TEXT,
  allowed BOOLEAN NOT NULL DEFAULT true,
  visited_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_browser_user
  ON public.browser_history (github_username, visited_at DESC);
CREATE INDEX IF NOT EXISTS idx_browser_blocked
  ON public.browser_history (allowed, visited_at DESC);

ALTER TABLE public.browser_history ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "anon_insert_browser" ON public.browser_history;
CREATE POLICY "anon_insert_browser" ON public.browser_history
  FOR INSERT WITH CHECK (true);

DROP POLICY IF EXISTS "auth_read_browser" ON public.browser_history;
CREATE POLICY "auth_read_browser" ON public.browser_history
  FOR SELECT TO authenticated USING (true);

SELECT 'browser_history' AS tabla, COUNT(*) FROM public.browser_history;
