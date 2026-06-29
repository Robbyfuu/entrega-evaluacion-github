-- ⚠️ FASE 2: EXIGE identidad verificada. RECHAZA clientes sin JWT (anon crudo).
-- NO CORRER hasta: (1) 0 clientes <2.7.20 activos (ENT-23) y (2) fuera de ventana de examen.
-- ============================================================
--  Migracion: TARGETED READ SCOPE -- FASE 2 (breaking: exige claim).
--  Idempotente. Correr en Supabase SQL Editor.
--
--  OBJETIVO (sec-backend-08, MEDIUM):
--  Hoy public.targeted_lockdowns expone UNA sola policy de lectura,
--  "anon_read_targeted", con:
--      FOR SELECT TO anon, authenticated USING (true)
--  Eso deja que CUALQUIER cliente con la apikey anon ENUMERE todas las
--  filas: que PCs / que alumnos estan bloqueados, sus reasons, etc.
--  (residual R3 documentado en migration-rls-identity-hardening.sql).
--
--  ARREGLO:
--  Partir esa lectura unica en DOS policies role-scoped, usando la
--  IDENTIDAD VERIFICADA del JWT (helper public.jwt_github_username(),
--  definido en migration-jwt-identity.sql: devuelve el claim verificado
--  o NULL si el cliente no esta enrolado / no manda JWT).
--
--    - targeted_read_teacher (TO authenticated, USING true):
--        el panel admin corre AUTHENTICATED y necesita ver TODAS las
--        filas (LockedStudentsSection). Queda intacto.
--
--    - targeted_read_student (TO anon, USING
--        github_username = jwt_github_username()):
--        el cliente enrolado solo ve SU propia fila (la consulta el con
--        GET filtrado por pc_name + github_username en SupabaseClient.cs:
--        IsTargetedLockedAsync / GetTargetedReasonAsync, nunca lista
--        todo). FASE 2: un cliente sin JWT (claim NULL, anon key cruda) ->
--        0 filas, deja de poder enumerar nada (ya NO hay backward-compat).
--
--  ALCANCE: esta migracion SOLO reemplaza la lectura anon de
--  targeted_lockdowns. NO toca INSERT/UPDATE/DELETE (auth_all_targeted,
--  ni la RPC report_self_lock que escribe via SECURITY DEFINER), NO toca
--  pc_overrides ni ninguna otra tabla/policy.
-- ============================================================


-- ============================================================
--  1. Reemplazo de la lectura anon amplia por dos policies
--     role-scoped. DROP IF EXISTS + CREATE para idempotencia.
-- ============================================================

-- Quita la policy vieja de lectura (anon + authenticated, USING true).
DROP POLICY IF EXISTS "anon_read_targeted" ON public.targeted_lockdowns;

-- Profe / panel admin: ve TODAS las filas (sesion authenticated).
DROP POLICY IF EXISTS "targeted_read_teacher" ON public.targeted_lockdowns;
CREATE POLICY "targeted_read_teacher" ON public.targeted_lockdowns
  FOR SELECT TO authenticated USING (true);

-- Cliente: SOLO ve su propia fila (FASE 2: se EXIGE el claim verificado; un
-- anon sin JWT -> jwt_github_username() NULL -> 0 filas, deja de ver todo).
DROP POLICY IF EXISTS "targeted_read_student" ON public.targeted_lockdowns;
CREATE POLICY "targeted_read_student" ON public.targeted_lockdowns
  FOR SELECT TO anon
  USING (github_username = public.jwt_github_username());


-- ============================================================
--  FASE 2 -- APLICADA EN ESTE ARCHIVO.
--  La policy targeted_read_student ya EXIGE identidad verificada: se
--  elimino la rama "claim NULL -> ve todo". Un anon sin JWT ->
--  jwt_github_username() NULL -> la comparacion da NULL -> 0 filas: deja de
--  poder enumerar nada. La policy del profe (targeted_read_teacher) queda
--  igual.
--
--  Precondicion para CORRERLO: TODOS los clientes enrolados con JWT (no
--  quedan clientes <2.7.20 activos, ENT-23) y fuera de ventana de examen.
--  Correrlo antes ciega a los clientes viejos sin JWT.
--
--  ROLLBACK A FASE 1 (backward-compat) si hay que revertir: reintroducir la
--  rama "claim NULL -> ve todo":
--    DROP POLICY IF EXISTS "targeted_read_student" ON public.targeted_lockdowns;
--    CREATE POLICY "targeted_read_student" ON public.targeted_lockdowns
--      FOR SELECT TO anon
--      USING (
--        public.jwt_github_username() IS NULL
--        OR github_username = public.jwt_github_username()
--      );
-- ============================================================


-- ============================================================
--  VERIFICACION
--     1) Existen las 2 policies role-scoped esperadas.
--     2) Ya NO queda ninguna policy de SELECT anon con USING(true)
--        sobre targeted_lockdowns (la enumeracion quedo cerrada).
--     3) (FASE 2) targeted_read_student ya NO contiene "IS NULL" en su qual
--        (se elimino la rama de backward-compat; esperado: 0 filas).
-- ============================================================

-- 1) Las 2 policies nuevas presentes con su rol correcto (esperado: 2 filas).
SELECT 'V1 policies role-scoped de lectura (esperado 2)' AS check,
       policyname, roles, qual
FROM pg_policies
WHERE schemaname = 'public'
  AND tablename = 'targeted_lockdowns'
  AND cmd = 'SELECT'
  AND policyname IN ('targeted_read_teacher', 'targeted_read_student')
ORDER BY policyname;

-- 2) Ya no hay lectura anon abierta (esperado: 0 filas).
SELECT 'V2 lectura anon abierta residual (esperado 0)' AS check,
       policyname
FROM pg_policies
WHERE schemaname = 'public'
  AND tablename = 'targeted_lockdowns'
  AND cmd = 'SELECT'
  AND 'anon' = ANY (roles)
  AND qual = 'true';

-- 3) (FASE 2) targeted_read_student sin rama "claim NULL" (esperado: 0 filas).
SELECT 'V3 targeted_read_student con IS NULL residual (esperado 0)' AS check,
       policyname
FROM pg_policies
WHERE schemaname = 'public'
  AND tablename = 'targeted_lockdowns'
  AND cmd = 'SELECT'
  AND policyname = 'targeted_read_student'
  AND qual ILIKE '%IS NULL%';
