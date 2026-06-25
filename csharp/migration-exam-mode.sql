-- ============================================================
--  Migracion: modo de evaluacion por evaluacion
--  - evaluations.exam_mode  : politica de bloqueo de la evaluacion.
--    Off       = sin aplicacion del modo examen.
--    AuditOnly = solo registra/observa, no bloquea.
--    SoftLock  = bloqueo suave (avisos, sin forzar).
--    HardLock  = bloqueo duro (forzado).
--    Default 'Off' para no cambiar el comportamiento de filas existentes.
--  - evaluations.policy_json : configuracion adicional del modo (JSONB),
--    nullable; las filas heredan NULL hasta que el profe la define.
--  No toca RLS: evaluations ya tiene policies.
--  Idempotente. Correr en Supabase SQL Editor.
-- ============================================================

-- 1. Columnas nuevas ----------------------------------------------------
ALTER TABLE public.evaluations
  ADD COLUMN IF NOT EXISTS exam_mode TEXT NOT NULL DEFAULT 'Off'
  CHECK (exam_mode IN ('Off','AuditOnly','SoftLock','HardLock'));

ALTER TABLE public.evaluations
  ADD COLUMN IF NOT EXISTS policy_json JSONB;

-- 2. Verificacion -------------------------------------------------------
SELECT 'evaluations.exam_mode' AS check, COUNT(*) AS filas_con_modo
FROM public.evaluations WHERE exam_mode IS NOT NULL
UNION ALL
SELECT 'evaluations.policy_json', COUNT(*)
FROM public.evaluations WHERE policy_json IS NOT NULL;
