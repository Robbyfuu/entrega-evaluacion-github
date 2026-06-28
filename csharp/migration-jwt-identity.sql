-- ⚠️ FASE 2: EXIGE identidad verificada. RECHAZA clientes sin JWT (anon crudo).
-- NO CORRER hasta: (1) 0 clientes <2.7.20 activos (ENT-23) y (2) fuera de ventana de examen.
-- ============================================================
--  Migracion: JWT IDENTITY HARDENING -- FASE 2 (breaking: exige claim).
--  Idempotente. Correr en Supabase SQL Editor.
--
--  OBJETIVO:
--  Cerrar la suplantacion de identidad (residual R1/R3 de
--  migration-rls-identity-hardening.sql) usando la IDENTIDAD VERIFICADA
--  que viaja en el JWT, sin romper a ningun cliente en vivo.
--
--  COMO LLEGA LA IDENTIDAD VERIFICADA:
--  La Edge Function `enroll-identity` (POST /functions/v1/enroll-identity)
--  recibe el github_token del device-flow del alumno, valida ese token
--  contra GET https://api.github.com/user y, con el login/id VERIFICADOS,
--  emite un JWT HS256 firmado con el JWT Secret legacy del proyecto. El
--  JWT lleva claims:
--      role            = "anon"   (NO "authenticated": mantiene las
--                                  policies anon actuales; "authenticated"
--                                  daria god-mode de profe)
--      aud             = "authenticated"
--      iss             = "exam-enroll"
--      github_username = <login verificado>
--      github_id       = <id verificado>
--      iat / exp       = ahora / ahora + 12h
--  El cliente porta ese JWT como `Authorization: Bearer <jwt>` (mas la
--  apikey anon) en TODAS las llamadas REST/RPC. PostgREST expone esos
--  claims a la sesion SQL via `request.jwt.claims`, asi que las RPC y las
--  policies pueden leer el github_username VERIFICADO y compararlo contra
--  el que el cliente AFIRMA por parametro/columna.
--
--  REGLA DE FASE 2 (EXIGE EL CLAIM VERIFICADO):
--    - claim github_username NULL/ausente o vacio (cliente sin enrolar o
--      con anon key cruda como Bearer) -> RECHAZADO (RPC: RETURN no-op;
--      policy: false).
--    - claim github_username presente pero DISTINTO del username afirmado
--      -> RECHAZADO.
--    - claim github_username presente e IGUAL al afirmado -> PERMITIDO.
--  De este modo SOLO un cliente con IDENTIDAD VERIFICADA puede escribir, y
--  unicamente a su propio nombre: se cierra de raiz la suplantacion (R1/R3).
--
--  ADVERTENCIA: esto ROMPE a clientes viejos sin JWT. No aplicar hasta que
--  no queden clientes <2.7.20 activos (ENT-23). El bloque al final conserva
--  la nota de ROLLBACK a la regla FASE 1 (backward-compat).
--
--  Esta migracion NO cambia firmas ni grants de RPC, NO toca otras tablas
--  ni otras policies, y conserva el cuerpo completo de cada RPC (guard de
--  identidad-presente, UPSERT keyed, rate-limit de process_alert).
-- ============================================================


-- ============================================================
--  0. HELPER: identidad verificada del JWT.
--     Devuelve el claim github_username del JWT verificado, o NULL si no
--     hay JWT/claim (cliente viejo con anon key cruda) o si el parseo
--     falla. STABLE: depende solo de la sesion, no escribe.
-- ============================================================
CREATE OR REPLACE FUNCTION public.jwt_github_username()
RETURNS TEXT
LANGUAGE plpgsql
STABLE
SET search_path = public
AS $$
DECLARE
  v_claim TEXT;
BEGIN
  -- current_setting(..., true) devuelve NULL si el GUC no esta seteado.
  -- El ::json ->> extrae el claim; cualquier error de parseo cae al EXCEPTION.
  v_claim := current_setting('request.jwt.claims', true)::json ->> 'github_username';
  RETURN v_claim;
EXCEPTION
  WHEN OTHERS THEN
    RETURN NULL;
END;
$$;

GRANT EXECUTE ON FUNCTION public.jwt_github_username() TO anon, authenticated;


-- ============================================================
--  A. RPCs ANON-CALLABLES: guard de IDENTIDAD-VERIFICADA.
--     CREATE OR REPLACE con la firma EXACTA de
--     migration-rls-identity-hardening.sql (nombre + tipos + orden +
--     defaults intactos; grants intactos). Se conserva todo el cuerpo
--     previo (guard de identidad-presente, UPSERT keyed, rate-limit) y se
--     SUMA el guard de identidad-verificada justo despues:
--
--         v := public.jwt_github_username();
--         IF v IS NULL OR v = '' OR v IS DISTINCT FROM p_github_username
--         THEN RETURN; END IF;   -- no-op silencioso; EXIGE claim verificado
--
--     Si el claim es NULL/ausente (cliente sin enrolar) la condicion
--     dispara y la escritura es RECHAZADA (FASE 2, ya no backward-compat).
-- ============================================================

-- A.1 record_acceptance (7 params). Firma IDENTICA.
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
DECLARE
  v_jwt_user TEXT;
BEGIN
  -- Guard de identidad-presente: sin username no se registra (no-op).
  IF p_github_username IS NULL OR btrim(p_github_username) = '' THEN
    RETURN;
  END IF;

  -- Guard de identidad-verificada (FASE 2): se EXIGE el claim verificado.
  -- Claim presente y DISTINTO del afirmado -> rechazo silencioso. Claim
  -- NULL/ausente (cliente sin enrolar) -> RECHAZADO (ya no es backward-compat).
  v_jwt_user := public.jwt_github_username();
  IF v_jwt_user IS NULL OR v_jwt_user = ''
     OR v_jwt_user IS DISTINCT FROM p_github_username THEN
    RETURN;
  END IF;

  INSERT INTO public.assignment_acceptances
    (github_username, assignment_id, assignment_title, section, repo_name, repo_url, evaluation_id, accepted_at)
  VALUES
    (p_github_username, p_assignment_id, p_assignment_title, p_section, p_repo_name, p_repo_url, p_evaluation_id, NOW())
  ON CONFLICT (github_username, assignment_id) DO UPDATE
  SET assignment_title = EXCLUDED.assignment_title,
      section          = EXCLUDED.section,
      repo_name        = EXCLUDED.repo_name,
      repo_url         = EXCLUDED.repo_url,
      evaluation_id    = COALESCE(EXCLUDED.evaluation_id, public.assignment_acceptances.evaluation_id),
      accepted_at      = NOW();
END;
$$;
GRANT EXECUTE ON FUNCTION public.record_acceptance(TEXT,BIGINT,TEXT,TEXT,TEXT,TEXT,BIGINT)
  TO anon, authenticated;

-- A.2 record_submission (4 params). Firma IDENTICA.
CREATE OR REPLACE FUNCTION public.record_submission(
  p_assignment_id BIGINT,
  p_github_username TEXT,
  p_repo_url TEXT,
  p_status TEXT DEFAULT 'submitted'
) RETURNS VOID
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
DECLARE
  v_jwt_user TEXT;
BEGIN
  -- Guard de identidad-presente.
  IF p_github_username IS NULL OR btrim(p_github_username) = '' THEN
    RETURN;
  END IF;

  -- Guard de identidad-verificada (FASE 2): se EXIGE el claim verificado.
  v_jwt_user := public.jwt_github_username();
  IF v_jwt_user IS NULL OR v_jwt_user = ''
     OR v_jwt_user IS DISTINCT FROM p_github_username THEN
    RETURN;
  END IF;

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

-- A.3 report_self_lock (5 params). Firma IDENTICA.
CREATE OR REPLACE FUNCTION public.report_self_lock(
  p_pc_name TEXT,
  p_github_username TEXT,
  p_section TEXT DEFAULT NULL,
  p_reason TEXT DEFAULT NULL,
  p_source TEXT DEFAULT 'trap'
) RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
  v_jwt_user TEXT;
BEGIN
  -- Guard de identidad-presente: sin pc_name + username no hay a quien
  -- atribuir el auto-lock (no-op). Reduce ruido, no cierra el residual.
  IF p_pc_name IS NULL OR btrim(p_pc_name) = ''
     OR p_github_username IS NULL OR btrim(p_github_username) = '' THEN
    RETURN;
  END IF;

  -- Guard de identidad-verificada (FASE 2): se EXIGE el claim verificado;
  -- evita que un alumno auto-bloquee la pantalla de un tercero, y ademas
  -- rechaza a cualquier cliente sin JWT (claim NULL/ausente).
  v_jwt_user := public.jwt_github_username();
  IF v_jwt_user IS NULL OR v_jwt_user = ''
     OR v_jwt_user IS DISTINCT FROM p_github_username THEN
    RETURN;
  END IF;

  INSERT INTO public.targeted_lockdowns
    (pc_name, github_username, active, reason, source, created_at, released_at)
  VALUES
    (p_pc_name, p_github_username, true, p_reason,
     COALESCE(p_source, 'trap'), NOW(), NULL)
  ON CONFLICT (pc_name, github_username) DO UPDATE
  SET active      = true,
      reason      = EXCLUDED.reason,
      source      = EXCLUDED.source,
      created_at  = NOW(),
      released_at = NULL;
END;
$$;
GRANT EXECUTE ON FUNCTION public.report_self_lock(TEXT, TEXT, TEXT, TEXT, TEXT)
  TO anon, authenticated;

-- A.4 report_process_alert (6 params, ORDEN load-bearing). Firma IDENTICA.
CREATE OR REPLACE FUNCTION public.report_process_alert(
  p_github_username TEXT,
  p_pc_name TEXT,
  p_section TEXT,
  p_process_name TEXT,
  p_window_title TEXT,
  p_evaluation_id BIGINT DEFAULT NULL
) RETURNS VOID
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
DECLARE
  v_jwt_user TEXT;
BEGIN
  -- Guard de identidad-presente: la alerta se atribuye a (pc_name +
  -- process_name); sin esos campos no se registra (no-op).
  IF p_pc_name IS NULL OR btrim(p_pc_name) = ''
     OR p_process_name IS NULL OR btrim(p_process_name) = '' THEN
    RETURN;
  END IF;

  -- Guard de identidad-verificada (FASE 2): se EXIGE el claim verificado.
  -- Claim presente y distinto del afirmado -> no se registra la alerta a
  -- nombre del tercero. Claim NULL/ausente -> RECHAZADO (sin backward-compat).
  v_jwt_user := public.jwt_github_username();
  IF v_jwt_user IS NULL OR v_jwt_user = ''
     OR v_jwt_user IS DISTINCT FROM p_github_username THEN
    RETURN;
  END IF;

  -- Rate-limit: descartar duplicado dentro de la ventana de 30s.
  IF EXISTS (
    SELECT 1 FROM public.process_alerts
    WHERE pc_name = p_pc_name
      AND process_name = p_process_name
      AND detected_at > NOW() - INTERVAL '30 seconds'
  ) THEN
    RETURN;
  END IF;

  INSERT INTO public.process_alerts
    (pc_name, github_username, section, process_name, window_title, evaluation_id, detected_at)
  VALUES
    (p_pc_name, p_github_username, p_section, p_process_name, p_window_title, p_evaluation_id, NOW());
END;
$$;
GRANT EXECUTE ON FUNCTION public.report_process_alert(TEXT,TEXT,TEXT,TEXT,TEXT,BIGINT)
  TO anon, authenticated;


-- ============================================================
--  B. POLICIES anon_insert de INSERT DIRECTO (sin RPC):
--     cheat_events / student_activity / browser_history.
--     El WITH CHECK original (true) dejaba a cualquier anon insertar
--     cualquier identidad. FASE 2 lo endurece a:
--         <col_identidad> = jwt_github_username()
--     -> un cliente CON JWT solo puede insertar a SU nombre verificado.
--     -> un cliente sin JWT (claim NULL) queda RECHAZADO (sin compat).
--     DROP POLICY IF EXISTS + CREATE para idempotencia.
--
--     OJO con la columna de identidad por tabla:
--       cheat_events      -> columna `username`
--       student_activity  -> columna `github_username`
--       browser_history   -> columna `github_username`
-- ============================================================

-- B.1 cheat_events (columna de identidad: username).
DROP POLICY IF EXISTS "anon_insert_cheat" ON public.cheat_events;
CREATE POLICY "anon_insert_cheat" ON public.cheat_events
  FOR INSERT
  WITH CHECK ( username = public.jwt_github_username() );

-- B.2 student_activity (columna de identidad: github_username).
DROP POLICY IF EXISTS "anon_insert_activity" ON public.student_activity;
CREATE POLICY "anon_insert_activity" ON public.student_activity
  FOR INSERT
  WITH CHECK ( github_username = public.jwt_github_username() );

-- B.3 browser_history (columna de identidad: github_username).
DROP POLICY IF EXISTS "anon_insert_browser" ON public.browser_history;
CREATE POLICY "anon_insert_browser" ON public.browser_history
  FOR INSERT
  WITH CHECK ( github_username = public.jwt_github_username() );


-- ============================================================
--  FASE 2 -- APLICADA EN ESTE ARCHIVO ("REQUERIR EL CLAIM").
--
--  Este archivo YA contiene la version FASE 2: las 4 RPC y las 3 policies
--  anon_insert_* EXIGEN el claim github_username verificado. Cualquier
--  escritura sin JWT verificado queda denegada y se cierra de raiz la
--  suplantacion (R1/R3).
--
--  Precondicion para CORRERLO: TODOS los clientes en produccion estan
--  enrolados via enroll-identity y portan el JWT con github_username
--  verificado (no quedan clientes <2.7.20 activos, ENT-23) y se corre fuera
--  de la ventana de examen. Correrlo antes ROMPE a los clientes viejos que
--  aun usan la anon key cruda.
--
--  ROLLBACK A FASE 1 (backward-compat) si hay que revertir:
--
--  1) En las 4 RPC, restaurar el guard que PERMITE el claim NULL:
--
--         v_jwt_user := public.jwt_github_username();
--         IF v_jwt_user IS NOT NULL AND v_jwt_user <> ''
--            AND v_jwt_user IS DISTINCT FROM p_github_username THEN
--           RETURN;
--         END IF;
--
--  2) En las 3 policies anon_insert_*, reintroducir el "IS NULL OR":
--
--         -- cheat_events
--         WITH CHECK ( public.jwt_github_username() IS NULL
--                      OR username = public.jwt_github_username() );
--         -- student_activity
--         WITH CHECK ( public.jwt_github_username() IS NULL
--                      OR github_username = public.jwt_github_username() );
--         -- browser_history
--         WITH CHECK ( public.jwt_github_username() IS NULL
--                      OR github_username = public.jwt_github_username() );
-- ============================================================


-- ============================================================
--  C. VERIFICACION
--     1) El helper existe y es ejecutable por anon.
--     2) Las 4 RPC siguen presentes y ejecutables por anon (firmas/grants
--        intactos).
--     3) Las 3 policies anon_insert_* ya NO usan WITH CHECK (true): su
--        expresion referencia jwt_github_username y (FASE 2) ya NO contiene
--        "IS NULL" como rama de backward-compat (esperado: 3 filas).
-- ============================================================

-- 1) Helper presente y ejecutable por anon.
SELECT 'C1 helper jwt_github_username ejecutable por anon' AS check,
       has_function_privilege('anon', 'public.jwt_github_username()', 'EXECUTE') AS ok;

-- 2) RPC endurecidas presentes y ejecutables por anon (esperado: 4 filas).
SELECT 'C2 grant EXECUTE a anon' AS check, p.proname AS rpc
FROM pg_proc p
JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname = 'public'
  AND p.proname IN ('record_acceptance', 'record_submission',
                    'report_self_lock', 'report_process_alert')
  AND has_function_privilege('anon', p.oid, 'EXECUTE')
ORDER BY p.proname;

-- 3) Policies anon_insert_* endurecidas (FASE 2): su WITH CHECK referencia el
--    helper y ya NO contiene "IS NULL" (esperado: 3 filas, una por tabla).
SELECT 'C3 anon_insert con guard JWT FASE 2 (esperado 3)' AS check,
       tablename, policyname
FROM pg_policies
WHERE schemaname = 'public'
  AND tablename IN ('cheat_events', 'student_activity', 'browser_history')
  AND policyname LIKE 'anon_insert_%'
  AND with_check ILIKE '%jwt_github_username%'
  AND with_check NOT ILIKE '%IS NULL%'
ORDER BY tablename;
