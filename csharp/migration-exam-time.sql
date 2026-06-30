-- ============================================================
--  Migracion: tiempo de examen por evaluacion + reloj de servidor
--  - evaluations.ends_at : hora FIJA de termino del examen, por
--    evaluacion, definida por el docente (analoga a exam_pdf_path /
--    exam_mode). NULL => la evaluacion no tiene cuenta regresiva
--    (el widget no muestra contador).
--  - RPC get_exam_time(p_evaluation_id) : devuelve la hora del
--    servidor (clock_timestamp()) + el ends_at de la evaluacion, para
--    que el widget del cliente compute el tiempo restante SIN confiar
--    en el reloj local de la maquina.
--  No toca RLS: evaluations ya tiene policies (anon lee solo activas,
--  authenticated CRUD). Ver decision de SECURITY abajo.
--  Idempotente. Correr en Supabase SQL Editor.
--
--  ORDEN DE EJECUCION: correr DESPUES de migration-multi-evaluation.sql
--  (provee la tabla evaluations y su RLS anon_read_evaluations
--  USING (active = true)) y junto al resto de las migraciones exam-*
--  (exam-mode / exam-pdf / exam-pdf-scope) que tambien ALTERan
--  evaluations de forma aditiva.
-- ============================================================

-- ============================================================
--  1. Columna nueva
--  ends_at nace NULL para todas las filas existentes (forward-
--  compatible): sin cuenta regresiva hasta que el docente fije una
--  hora de termino desde el panel. NULL = el widget no muestra
--  contador. Mismo patron aditivo que exam_pdf_path / exam_mode.
-- ============================================================

ALTER TABLE public.evaluations
  ADD COLUMN IF NOT EXISTS ends_at TIMESTAMPTZ;

-- ============================================================
--  2. RPC get_exam_time : reloj de servidor + ends_at
--
--  DECISION DE SECURITY: SECURITY INVOKER (least-privilege).
--  La funcion corre con los privilegios y la RLS del rol que la
--  invoca, de modo que el SELECT interno queda sujeto a la policy
--  anon_read_evaluations (USING active = true). Asi:
--    - anon obtiene server_now + ends_at SOLO para evaluaciones
--      ACTIVAS (justo cuando el widget lo necesita); para una
--      evaluacion inactiva o inexistente el SELECT no ve la fila y
--      la RPC devuelve 0 filas (el widget no muestra contador).
--    - authenticated (panel) ve cualquier evaluacion via
--      auth_all_evaluations.
--  El cliente del alumno ya lee public.evaluations con la anon key
--  (GET evaluations?...&select=*), o sea anon ya tiene SELECT a nivel
--  de tabla; por eso INVOKER funciona sin GRANT adicional y NO hace
--  falta SECURITY DEFINER. Se evita DEFINER a proposito: bypassearia
--  la RLS y filtraria ends_at de evaluaciones NO activas a anon, sin
--  necesidad. Mismo criterio active-only que exam-pdf-scope.sql.
--
--  A DIFERENCIA de get_my_enrollment (DEFINER, porque enrollments es
--  authenticated-ONLY y guarda PII), aca la tabla ya tiene lectura
--  anon acotada por active, asi que INVOKER es suficiente y mas seguro.
--
--  NOTA: NO lleva guard de identidad FASE-2 (jwt_github_username()).
--  ends_at + server_now NO son sensibles, y exigir el claim cegaria al
--  widget ANTES del enrolamiento. Por eso esta RPC no va en el bloque
--  FASE 2 de migrations.order.
--
--  Se usa clock_timestamp() (hora real del statement), NO now() /
--  transaction_timestamp() (hora del inicio de la transaccion), para
--  entregar el instante mas fresco posible del servidor.
--
--  DROP previo de la firma para idempotencia ante un cambio futuro del
--  RETURNS TABLE (CREATE OR REPLACE no puede cambiar el tipo de
--  retorno). Mismo patron que get_my_enrollment.
-- ============================================================

DROP FUNCTION IF EXISTS public.get_exam_time(BIGINT);

CREATE OR REPLACE FUNCTION public.get_exam_time(
  p_evaluation_id BIGINT
) RETURNS TABLE (
  server_now TIMESTAMPTZ,
  ends_at TIMESTAMPTZ
)
LANGUAGE sql
SECURITY INVOKER
SET search_path = public
AS $$
  SELECT clock_timestamp(), e.ends_at
  FROM public.evaluations e
  WHERE e.id = p_evaluation_id;
$$;

GRANT EXECUTE ON FUNCTION public.get_exam_time(BIGINT)
  TO anon, authenticated;

-- ============================================================
--  3. VERIFICACION
--     1) La columna evaluations.ends_at existe (esperado: 1).
--     2) La RPC get_exam_time existe y es ejecutable por anon
--        (esperado: 1).
-- ============================================================

SELECT 'evaluations.ends_at existe' AS check, COUNT(*) AS n
  FROM information_schema.columns
  WHERE table_schema = 'public'
    AND table_name = 'evaluations'
    AND column_name = 'ends_at'
UNION ALL
SELECT 'get_exam_time EXECUTE anon', COUNT(*)
  FROM pg_proc p
  JOIN pg_namespace n ON n.oid = p.pronamespace
  WHERE n.nspname = 'public'
    AND p.proname = 'get_exam_time'
    AND has_function_privilege('anon', p.oid, 'EXECUTE');
