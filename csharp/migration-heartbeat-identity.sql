-- ============================================================
--  Migracion: identidad-verificada en heartbeat (sec-backend-05)
--
--  Problema (MEDIUM): la RPC heartbeat acepta identidad arbitraria via
--  p_github_username auto-asertado -> spoofing/flooding de presencia
--  (un cliente puede registrar/actualizar presencia en nombre de otro).
--
--  Fix: agregar el MISMO guard de identidad-verificada que ya tienen las
--  otras 4 RPC (record_acceptance, record_submission, report_self_lock,
--  report_process_alert) via public.jwt_github_username() — helper que
--  devuelve el claim github_username verificado del JWT (o NULL para el
--  cliente viejo). Definido en csharp/migration-jwt-identity.sql.
--
--  FASE 1 (esta migracion, compat): si el JWT trae un username verificado
--  y NO coincide con el afirmado, rechazo silencioso (no-op). Claim NULL
--  (cliente viejo, sin el claim en el JWT) -> se permite, comportamiento
--  identico al de hoy (backward-compat).
--
--  FASE 2 (futuro, flip a exigir claim): una vez que todos los clientes
--  emitan el claim github_username, endurecer el guard para EXIGIR el
--  claim, p.ej.:
--      IF v_jwt_user IS NULL OR v_jwt_user = '' THEN RETURN; END IF;
--      IF v_jwt_user IS DISTINCT FROM p_github_username THEN RETURN; END IF;
--  (es decir, dejar de aceptar el claim NULL como pase libre).
--
--  Idempotente. Correr en Supabase SQL Editor.
-- ============================================================

-- La firma es IDENTICA a la ultima definicion vigente (9 args, espejo de
-- csharp/migration-version-visibility.sql), por lo que CREATE OR REPLACE
-- reemplaza la funcion EN SU LUGAR: no crea un overload nuevo ni rompe a
-- PostgREST (300 ambiguous). Por eso NO hace falta DROP previo: una firma
-- distinta requeriria DROP, una identica no. El cuerpo (UPSERT a
-- online_clients ON CONFLICT (pc_name, github_username), columnas, COALESCE
-- de evaluation_id y app_version) queda INTACTO; solo se antepone el guard.
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
DECLARE
  v_jwt_user TEXT;
BEGIN
  -- Guard de identidad-verificada (FASE 1): si el JWT trae un username
  -- verificado y NO coincide con el afirmado, rechazo silencioso. Claim
  -- NULL (cliente viejo) -> se permite (backward-compat).
  v_jwt_user := public.jwt_github_username();
  IF v_jwt_user IS NOT NULL AND v_jwt_user <> ''
     AND v_jwt_user IS DISTINCT FROM p_github_username THEN
    RETURN;
  END IF;

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

-- ============================================================
--  VERIFICACION
--    1) La funcion heartbeat existe y es ejecutable por anon (firma/grant
--       intactos: exactamente UNA firma por nombre).
-- ============================================================

-- 1) heartbeat presente y ejecutable por anon (esperado: ok = true).
SELECT 'V1 heartbeat ejecutable por anon' AS check,
       has_function_privilege(
         'anon',
         'public.heartbeat(TEXT,TEXT,TEXT,TEXT,JSONB,TEXT,TEXT,BIGINT,TEXT)',
         'EXECUTE') AS ok;
