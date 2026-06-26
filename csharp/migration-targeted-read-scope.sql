-- ============================================================
--  Migracion: TARGETED READ SCOPE -- FASE 1 (no-breaking).
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
--    - targeted_read_student (TO anon, USING jwt_github_username() IS NULL
--        OR github_username = jwt_github_username()):
--        el cliente enrolado solo ve SU propia fila (la consulta el con
--        GET filtrado por pc_name + github_username en SupabaseClient.cs:
--        IsTargetedLockedAsync / GetTargetedReasonAsync, nunca lista
--        todo). Un cliente VIEJO (claim NULL, anon key cruda) sigue viendo
--        todo = backward-compat, no se rompe nada en FASE 1.
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

-- Cliente: enrolado ve SOLO su fila; viejo (claim NULL) ve todo (compat).
DROP POLICY IF EXISTS "targeted_read_student" ON public.targeted_lockdowns;
CREATE POLICY "targeted_read_student" ON public.targeted_lockdowns
  FOR SELECT TO anon
  USING (
    public.jwt_github_username() IS NULL
    OR github_username = public.jwt_github_username()
  );


-- ============================================================
--  FASE 2 (aplicar cuando TODOS los clientes esten enrolados con JWT):
--  eliminar la rama "claim NULL -> ve todo" para EXIGIR identidad
--  verificada en toda lectura anon. La policy del profe queda igual.
--
--    DROP POLICY IF EXISTS "targeted_read_student" ON public.targeted_lockdowns;
--    CREATE POLICY "targeted_read_student" ON public.targeted_lockdowns
--      FOR SELECT TO anon
--      USING (github_username = public.jwt_github_username());
--
--  (Con esto un anon sin JWT -> jwt_github_username() NULL -> la
--   comparacion da NULL -> 0 filas: deja de poder leer nada.)
-- ============================================================


-- ============================================================
--  VERIFICACION
--     1) Existen las 2 policies role-scoped esperadas.
--     2) Ya NO queda ninguna policy de SELECT anon con USING(true)
--        sobre targeted_lockdowns (la enumeracion quedo cerrada).
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
