-- ============================================================
--  Migracion: bloqueo de Copilot / IA como control independiente
--  Idempotente. Correr en Supabase SQL Editor.
--
--  Capa aditiva ENCIMA de setup-supabase.sql (control global id=1)
--  y de migration-evaluation-control.sql (override por evaluacion).
--
--  ORDEN DE EJECUCION: correr DESPUES de setup-supabase.sql
--  (provee la tabla control + el control global id=1) y DESPUES de
--  migration-evaluation-control.sql (provee evaluation_control).
--
--  Esta migracion agrega un flag copilot_block para armar el bloqueo
--  de Copilot / IA de VS Code de forma INDEPENDIENTE de internet_block.
--  Semantica ADITIVA en el cliente: Copilot se bloquea cuando
--  internet_block O copilot_block estan activos (no exclusivo), asi el
--  modo examen actual (Copilot esclavo de internet_block) se preserva
--  y ademas se gana el switch standalone del panel.
--
--    1. control.copilot_block: flag GLOBAL (id=1). DEFAULT false =>
--       el comportamiento existente se preserva (sin regresion).
--    2. evaluation_control.copilot_block: override POR EVALUACION,
--       NULLABLE (NULL = heredar el valor del control global).
--
--  NO destructivo: solo ADD COLUMN IF NOT EXISTS. No toca RLS ni
--  realtime: las policies de ambas tablas son a nivel de tabla
--  (no por columna) y la publicacion supabase_realtime ya incluye
--  la tabla completa, asi que las nuevas columnas quedan cubiertas.
--  Los clientes legacy que no leen copilot_block siguen funcionando.
-- ============================================================

-- ============================================================
--  1. control.copilot_block (GLOBAL id=1)
--  NOT NULL DEFAULT false: las filas existentes (incluido id=1)
--  toman false => el bloqueo de Copilot sigue dependiendo solo de
--  internet_block como hasta ahora (sin regresion).
-- ============================================================

ALTER TABLE public.control
  ADD COLUMN IF NOT EXISTS copilot_block BOOLEAN NOT NULL DEFAULT false;

-- ============================================================
--  2. evaluation_control.copilot_block (override POR EVALUACION)
--  NULLABLE: NULL = heredar el copilot_block del control global id=1.
--  Mismo patron que internet_block / force_lockdown en esta tabla.
-- ============================================================

ALTER TABLE public.evaluation_control
  ADD COLUMN IF NOT EXISTS copilot_block BOOLEAN;

-- ============================================================
--  3. VERIFICACION
-- ============================================================

DO $$
DECLARE
  has_control_col   BOOLEAN;
  has_eval_col      BOOLEAN;
BEGIN
  SELECT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'public' AND table_name = 'control'
      AND column_name = 'copilot_block'
  ) INTO has_control_col;

  SELECT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'public' AND table_name = 'evaluation_control'
      AND column_name = 'copilot_block'
  ) INTO has_eval_col;

  IF NOT has_control_col THEN
    RAISE EXCEPTION 'FALLO: public.control.copilot_block no se creo';
  END IF;
  IF NOT has_eval_col THEN
    RAISE EXCEPTION 'FALLO: public.evaluation_control.copilot_block no se creo';
  END IF;

  RAISE NOTICE 'OK: copilot_block presente en control y evaluation_control';
END $$;

SELECT 'control_global_copilot_block' AS dato, copilot_block::text AS valor
  FROM public.control WHERE id = 1
UNION ALL
SELECT 'evaluation_control_overrides_copilot', COUNT(*)::text
  FROM public.evaluation_control WHERE copilot_block IS NOT NULL;
