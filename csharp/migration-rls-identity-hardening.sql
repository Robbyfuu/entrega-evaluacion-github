-- ============================================================
--  Migracion: HARDENING de RLS/RPC para cerrar A01 (Broken Access
--  Control) sin romper el cliente en vivo (temporada de examenes).
--  Idempotente. Correr en Supabase SQL Editor.
--
--  CONTEXTO DE IDENTIDAD (leer antes de juzgar el alcance):
--  El cliente del alumno usa la ANON KEY COMPARTIDA. El github_username
--  y el pc_name van como PARAMETRO auto-asertado; NO hay sesion Supabase
--  por-alumno ni auth.uid() invocable. Por lo tanto NO existe binding
--  real de identidad a nivel RLS: cualquier anon puede afirmar cualquier
--  username. Esta migracion NO pretende cerrar ese gap (ver bloque
--  RESIDUAL al final); su objetivo es REDUCIR EL RADIO DE IMPACTO con
--  cambios de BAJO RIESGO que no rompen el cliente:
--
--    1. Eliminar el vector de SOBREESCRITURA / borrado de filas ajenas:
--       revocar el UPDATE (y dejar el DELETE ya denegado) directo de anon
--       sobre assignment_acceptances y assignment_submissions, donde
--       USING(true)/WITH CHECK(true) hoy deja a cualquier anon PISAR la
--       fila de OTRO alumno. El cliente nunca escribe estas tablas
--       directo: usa SIEMPRE las RPC SECURITY DEFINER record_acceptance /
--       record_submission (ver mapa de superficie de escritura). Por eso
--       revocar el INSERT/UPDATE directo NO rompe nada.
--
--    2. Forzar que TODA escritura de alumno sobre esas tablas pase por la
--       RPC, que hace UPSERT keyed por (github_username, assignment_id) y
--       por construccion NUNCA toca la fila de otra identidad salvo la que
--       el llamador afirma (residual inevitable, documentado).
--
--    3. Mantener cheat_events / student_activity / browser_history como
--       INSERT-only para anon (sin UPDATE/DELETE): no tienen RPC, el
--       cliente inserta directo, asi que solo blindamos contra mutacion
--       posterior de filas ajenas.
--
--    4. Endurecer las RPC anon-callables con un guard de identidad-presente
--       (rechazo silencioso de escrituras sin username/pc_name) sin alterar
--       su firma exacta (nombre + tipos + orden + defaults), que es
--       load-bearing para el POST del cliente.
--
--  NO se toca: heartbeat (presencia efimera, 9-arg load-bearing cada 20s),
--  process_alerts INSERT directo de anon (DROP DIFERIDO a proposito por
--  clientes v2.5.0 vivos), ni ningun GRANT/firma de RPC. Ver notas inline.
-- ============================================================


-- ============================================================
--  A. assignment_acceptances
--     Cierra: anon podia PISAR (UPDATE) la aceptacion de cualquier alumno
--     porque anon_update_acceptances usa USING(true) WITH CHECK(true) sin
--     binding de identidad. Tambien podia crear filas directo.
--     Por que NO rompe: el cliente registra aceptaciones EXCLUSIVAMENTE
--     via RPC record_acceptance (SECURITY DEFINER, hace UPSERT). De esta
--     tabla solo hace GET (lectura). Mantenemos anon_read_acceptances.
-- ============================================================

-- Revocar UPDATE directo de anon (vector de sobreescritura de fila ajena).
DROP POLICY IF EXISTS "anon_update_acceptances" ON public.assignment_acceptances;

-- Revocar INSERT directo de anon: la unica via de escritura es la RPC.
-- (La RPC es SECURITY DEFINER: inserta aunque no exista policy anon.)
DROP POLICY IF EXISTS "anon_insert_acceptances" ON public.assignment_acceptances;

-- Re-afirmar la lectura anon (sin cambios; necesaria para el cliente).
DROP POLICY IF EXISTS "anon_read_acceptances" ON public.assignment_acceptances;
CREATE POLICY "anon_read_acceptances" ON public.assignment_acceptances
  FOR SELECT TO anon, authenticated USING (true);

-- NOTA DELETE: no existe policy FOR DELETE de anon, por lo que el DELETE
-- ya esta denegado bajo RLS. No hace falta accion.
--
-- ROLLBACK (si se detecta un cliente legacy que escribia directo):
--   CREATE POLICY "anon_insert_acceptances" ON public.assignment_acceptances
--     FOR INSERT WITH CHECK (true);
--   CREATE POLICY "anon_update_acceptances" ON public.assignment_acceptances
--     FOR UPDATE USING (true) WITH CHECK (true);


-- ============================================================
--  B. assignment_submissions
--     Cierra y por que NO rompe: identico a (A). El cliente entrega via
--     RPC record_submission. Mantenemos solo la lectura anon.
-- ============================================================

DROP POLICY IF EXISTS "anon_update_submissions" ON public.assignment_submissions;
DROP POLICY IF EXISTS "anon_insert_submissions" ON public.assignment_submissions;

DROP POLICY IF EXISTS "anon_read_submissions" ON public.assignment_submissions;
CREATE POLICY "anon_read_submissions" ON public.assignment_submissions
  FOR SELECT TO anon, authenticated USING (true);

-- ROLLBACK:
--   CREATE POLICY "anon_insert_submissions" ON public.assignment_submissions
--     FOR INSERT WITH CHECK (true);
--   CREATE POLICY "anon_update_submissions" ON public.assignment_submissions
--     FOR UPDATE USING (true) WITH CHECK (true);


-- ============================================================
--  C. cheat_events / student_activity / browser_history
--     Estas tablas NO tienen RPC: el cliente inserta DIRECTO (writes
--     #6/#7/#8 del mapa). Las dejamos INSERT-only para anon. Hoy ya NO
--     tienen policy de UPDATE/DELETE, asi que la sobreescritura de filas
--     ajenas ya esta denegada; aqui solo lo BLINDAMOS de forma defensiva
--     e idempotente (por si una DB de produccion tuviera alguna policy
--     anon de mutacion creada a mano fuera del estado canonico) y
--     re-afirmamos el INSERT que el cliente necesita.
--     Por que NO rompe: el INSERT anon sigue vivo con las mismas columnas.
-- ============================================================

-- cheat_events
DROP POLICY IF EXISTS "anon_update_cheat" ON public.cheat_events;
DROP POLICY IF EXISTS "anon_delete_cheat" ON public.cheat_events;
DROP POLICY IF EXISTS "anon_insert_cheat" ON public.cheat_events;
CREATE POLICY "anon_insert_cheat" ON public.cheat_events
  FOR INSERT WITH CHECK (true);

-- student_activity
DROP POLICY IF EXISTS "anon_update_activity" ON public.student_activity;
DROP POLICY IF EXISTS "anon_delete_activity" ON public.student_activity;
DROP POLICY IF EXISTS "anon_insert_activity" ON public.student_activity;
CREATE POLICY "anon_insert_activity" ON public.student_activity
  FOR INSERT WITH CHECK (true);

-- browser_history
DROP POLICY IF EXISTS "anon_update_browser" ON public.browser_history;
DROP POLICY IF EXISTS "anon_delete_browser" ON public.browser_history;
DROP POLICY IF EXISTS "anon_insert_browser" ON public.browser_history;
CREATE POLICY "anon_insert_browser" ON public.browser_history
  FOR INSERT WITH CHECK (true);


-- ============================================================
--  D. process_alerts  (NO SE TOCA -- DROP DIFERIDO A PROPOSITO)
--     El INSERT directo de anon sobre process_alerts (anon_insert_alerts)
--     puede seguir vivo en una DB de produccion porque clientes v2.5.0 aun
--     insertan directo y se tragan el error en catch silencioso. Quitarlo
--     aqui haria desaparecer alertas SIN AVISO en cada PC no actualizado.
--     La RPC report_process_alert (SECURITY DEFINER, rate-limit 30s) y el
--     INSERT directo COEXISTEN sin problema. El DROP definitivo va recien
--     cuando PR2 (cliente -> RPC) este desplegado en TODAS las maquinas.
--     Ver la secuencia en migration-blocklist.sql seccion 5 y la nota en
--     setup-supabase.sql. (Sin cambios en esta migracion.)
-- ============================================================
-- (intencionalmente vacio)


-- ============================================================
--  E. ENDURECIMIENTO DE RPCs ANON-CALLABLES
--     Se re-afirman con CREATE OR REPLACE (firma EXACTA e intacta) los
--     UPSERT keyed para PIN del comportamiento seguro canonico + un guard
--     de IDENTIDAD-PRESENTE: si el llamador no afirma identidad (username/
--     pc_name vacio o NULL) la RPC hace RETURN silencioso (no escribe, no
--     lanza error -> no rompe el cliente, que envuelve el POST en
--     try/catch). Esto evita filas "fantasma" sin identidad atribuible.
--     RESIDUAL: un anon puede afirmar el username/pc_name de OTRO alumno;
--     eso NO es cerrable con RLS (ver bloque RESIDUAL). El UPSERT sigue
--     keyed por (github_username, assignment_id) / (pc_name, github_username)
--     asi que la escritura nunca toca una fila de identidad distinta a la
--     afirmada.
-- ============================================================

-- E.1 record_acceptance: UPSERT keyed por (github_username, assignment_id).
--     Firma IDENTICA a migration-acceptances.sql (7 params). Solo cambia el
--     cuerpo para sumar el guard de identidad-presente.
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
  -- Guard de identidad-presente: sin username no se registra (no-op).
  IF p_github_username IS NULL OR btrim(p_github_username) = '' THEN
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

-- E.2 record_submission: UPSERT keyed por (github_username, assignment_id).
--     Firma IDENTICA a migration-submissions.sql (4 params).
CREATE OR REPLACE FUNCTION public.record_submission(
  p_assignment_id BIGINT,
  p_github_username TEXT,
  p_repo_url TEXT,
  p_status TEXT DEFAULT 'submitted'
) RETURNS VOID
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  -- Guard de identidad-presente.
  IF p_github_username IS NULL OR btrim(p_github_username) = '' THEN
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

-- E.3 report_self_lock: solo fija active=true (el alumno NO puede
--     auto-liberarse; el release es authenticated). Firma IDENTICA a
--     migration-self-lock.sql (5 params). UPSERT keyed por
--     (pc_name, github_username).
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
BEGIN
  -- Guard de identidad-presente: sin pc_name + username no hay a quien
  -- atribuir el auto-lock (no-op). Reduce ruido, no cierra el residual.
  IF p_pc_name IS NULL OR btrim(p_pc_name) = ''
     OR p_github_username IS NULL OR btrim(p_github_username) = '' THEN
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

-- E.4 report_process_alert: rate-limit 30s por (pc_name, process_name).
--     Firma IDENTICA a migration-blocklist.sql / setup-supabase.sql
--     (6 params, ORDEN load-bearing: github_username, pc_name, section,
--     process_name, window_title, evaluation_id).
CREATE OR REPLACE FUNCTION public.report_process_alert(
  p_github_username TEXT,
  p_pc_name TEXT,
  p_section TEXT,
  p_process_name TEXT,
  p_window_title TEXT,
  p_evaluation_id BIGINT DEFAULT NULL
) RETURNS VOID
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  -- Guard de identidad-presente: la alerta se atribuye a (pc_name +
  -- process_name); sin esos campos no se registra (no-op).
  IF p_pc_name IS NULL OR btrim(p_pc_name) = ''
     OR p_process_name IS NULL OR btrim(p_process_name) = '' THEN
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
--  RESIDUAL CONOCIDO (NO cerrable solo con RLS por falta de identidad real)
--
--  Como el cliente usa la ANON KEY compartida y el github_username/pc_name
--  van auto-asertados por parametro, persisten estos vectores que ESTA
--  migracion NO puede cerrar (se documentan para el siguiente PR):
--
--   R1. Suplantacion de identidad en RPC: un anon puede llamar
--       record_acceptance / record_submission / report_self_lock /
--       report_process_alert afirmando el username/pc_name de OTRO alumno.
--       Impacto acotado: las RPC son keyed-upsert, asi que a lo sumo se
--       crea/pisa la fila DE LA IDENTIDAD AFIRMADA (p.ej. marcar una
--       entrega a nombre de un tercero, o auto-bloquear la pantalla de un
--       tercero via report_self_lock con su pc_name+username). El guard de
--       identidad-presente solo descarta escrituras SIN identidad, no las
--       de identidad ajena.
--
--   R2. heartbeat (presencia, online_clients) tiene el mismo gap; no se
--       endurece aqui por ser dato efimero y por su firma de 9 args
--       load-bearing cada 20s (riesgo > beneficio).
--
--   R3. Lecturas anon amplias: anon_read_targeted / anon_read_acceptances /
--       anon_read_submissions usan USING(true), de modo que un anon puede
--       LEER filas de otros alumnos. Acotar la lectura requiere binding de
--       identidad y rompe el cliente actual, asi que se deja como residual.
--
--  RECOMENDACION DE FONDO (trabajo futuro, fuera de esta migracion):
--  emitir un JWT POR DISPOSITIVO con un claim de identidad (device_id /
--  github_username verificado) via una Edge Function de enrolamiento, en
--  vez de la anon key compartida. Con ese JWT, las RPC y las policies
--  pueden comparar el parametro afirmado contra el claim (auth.jwt() ->>
--  'github_username') y RECHAZAR la suplantacion server-side, cerrando
--  R1/R2/R3 de raiz. Mientras tanto, esta migracion solo reduce el radio
--  de impacto (sobreescritura/borrado de filas ajenas y filas sin
--  identidad).
-- ============================================================


-- ============================================================
--  F. VERIFICACION
--     1) Las policies de mutacion directa de anon sobre acceptances/
--        submissions deben estar AUSENTES (esperado: 0 filas).
--     2) El INSERT-only de anon sobre cheat/activity/browser debe seguir
--        presente (esperado: 3 policies INSERT, 0 de UPDATE/DELETE anon).
--     3) Las 4 RPC endurecidas deben existir con EXECUTE para anon.
-- ============================================================

-- 1) No deben quedar policies de INSERT/UPDATE/DELETE de anon en estas tablas.
SELECT 'F1 mutacion directa anon (esperado 0)' AS check,
       COUNT(*) AS policies_restantes
FROM pg_policies
WHERE schemaname = 'public'
  AND tablename IN ('assignment_acceptances', 'assignment_submissions')
  AND cmd IN ('INSERT', 'UPDATE', 'DELETE');

-- 2) INSERT-only de anon vigente; sin UPDATE/DELETE de anon.
SELECT 'F2 insert anon vigente (esperado 3)' AS check,
       COUNT(*) FILTER (WHERE cmd = 'INSERT') AS inserts,
       COUNT(*) FILTER (WHERE cmd IN ('UPDATE', 'DELETE')) AS mutaciones_anon
FROM pg_policies
WHERE schemaname = 'public'
  AND tablename IN ('cheat_events', 'student_activity', 'browser_history')
  AND policyname LIKE 'anon_%';

-- 3) RPC endurecidas presentes y ejecutables por anon.
SELECT 'F3 grant EXECUTE a anon' AS check, p.proname AS rpc
FROM pg_proc p
JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname = 'public'
  AND p.proname IN ('record_acceptance', 'record_submission',
                    'report_self_lock', 'report_process_alert')
  AND has_function_privilege('anon', p.oid, 'EXECUTE')
ORDER BY p.proname;
