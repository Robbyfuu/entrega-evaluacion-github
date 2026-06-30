-- ============================================================
--  Migracion: entrega adicional en Blackboard por evaluacion
--  - evaluations.requires_blackboard : la evaluacion exige una entrega
--    adicional en Blackboard (DUOC AVA) ademas del push a GitHub. Cuando
--    es true, el cliente, tras subir, arma el ZIP del proyecto, abre la
--    carpeta, abre Blackboard en el navegador embebido y guia al alumno
--    para que suba el zip + pegue el link. Default false para NO cambiar
--    el comportamiento de las filas existentes (entrega normal intacta).
--  No toca RLS: evaluations ya es legible por anon (filas activas) y tiene
--  sus policies a nivel de tabla.
--  Idempotente. Correr en Supabase SQL Editor.
-- ============================================================

-- 1. Columna nueva ------------------------------------------------------
ALTER TABLE public.evaluations
  ADD COLUMN IF NOT EXISTS requires_blackboard BOOLEAN NOT NULL DEFAULT false;

-- 2. Verificacion -------------------------------------------------------
SELECT 'evaluations.requires_blackboard' AS check, COUNT(*) AS filas_con_flag
FROM public.evaluations WHERE requires_blackboard IS NOT NULL;
