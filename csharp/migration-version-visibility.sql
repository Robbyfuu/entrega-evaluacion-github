-- ============================================================
--  Migracion: visibilidad de version
--  - online_clients.app_version : version que corre cada alumno (la
--    reporta el cliente en el heartbeat -> el panel la muestra por PC).
--  - control.update_requested_at : el profe lo setea = NOW() desde el
--    panel para pedir a los clientes que actualicen (update MANUAL,
--    disparado; el cliente NO hace fetch automatico a GitHub).
--  Idempotente. Correr en Supabase SQL Editor.
-- ============================================================

-- 1. Columnas nuevas ----------------------------------------------------
ALTER TABLE public.online_clients
  ADD COLUMN IF NOT EXISTS app_version TEXT;

ALTER TABLE public.control
  ADD COLUMN IF NOT EXISTS update_requested_at TIMESTAMPTZ;

-- 2. heartbeat RPC: sumar p_app_version --------------------------------
-- p_app_version es nullable (DEFAULT NULL) para que clientes viejos que
-- llaman con la firma de 8 args sigan resolviendo. DROP previo de la firma
-- vieja (8 params) antes del CREATE: con una firma distinta CREATE OR
-- REPLACE crearia un NUEVO signature en vez de reemplazar, dejando dos
-- funciones vivas y rompiendo a PostgREST (300 ambiguous). UNA firma por
-- nombre. El UPDATE usa COALESCE para no pisar la version con NULL cuando
-- un cliente viejo (que no la manda) hace heartbeat.
DROP FUNCTION IF EXISTS public.heartbeat(
  TEXT, TEXT, TEXT, TEXT, JSONB, TEXT, TEXT, BIGINT);

CREATE OR REPLACE FUNCTION public.heartbeat(
  p_pc_name TEXT,
  p_github_username TEXT,
  p_github_email TEXT DEFAULT NULL,
  p_section TEXT DEFAULT NULL,
  p_processes JSONB DEFAULT '[]'::jsonb,
  p_internet_state TEXT DEFAULT 'free',
  p_lockdown_state TEXT DEFAULT 'none',
  p_evaluation_id BIGINT DEFAULT NULL,
  p_app_version TEXT DEFAULT NULL
) RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
  INSERT INTO public.online_clients
    (pc_name, github_username, github_email, section, processes,
     internet_state, lockdown_state, evaluation_id, app_version, last_seen)
  VALUES
    (p_pc_name, p_github_username, p_github_email, p_section, p_processes,
     p_internet_state, p_lockdown_state, p_evaluation_id, p_app_version, NOW())
  ON CONFLICT (pc_name, github_username) DO UPDATE
  SET github_email   = EXCLUDED.github_email,
      section        = EXCLUDED.section,
      processes      = EXCLUDED.processes,
      internet_state = EXCLUDED.internet_state,
      lockdown_state = EXCLUDED.lockdown_state,
      evaluation_id  = COALESCE(EXCLUDED.evaluation_id, public.online_clients.evaluation_id),
      app_version    = COALESCE(EXCLUDED.app_version, public.online_clients.app_version),
      last_seen      = NOW();
END;
$$;

GRANT EXECUTE ON FUNCTION public.heartbeat(TEXT,TEXT,TEXT,TEXT,JSONB,TEXT,TEXT,BIGINT,TEXT)
  TO anon, authenticated;

-- 3. Verificacion -------------------------------------------------------
SELECT 'online_clients.app_version' AS check, COUNT(*) AS filas_con_version
FROM public.online_clients WHERE app_version IS NOT NULL
UNION ALL
SELECT 'control.update_requested_at', COUNT(*)
FROM public.control WHERE update_requested_at IS NOT NULL;
