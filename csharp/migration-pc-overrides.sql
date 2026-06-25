-- ============================================================
--  Migracion: override de desbloqueo POR PC (por nombre de maquina)
--
--  Permite al profe desbloquear internet + pantalla roja de UN PC
--  identificado por su NOMBRE de maquina, sin depender del usuario
--  que tenga la sesion abierta (en el lab los alumnos rotan de PC).
--  El cliente C# lee public.pc_overrides por pc_name y, en <20s,
--  libera internet y/o saca la pantalla roja de ESE PC. Para que el
--  PC vuelva a poder bloquearse, el profe "reactiva monitoreo"
--  (unblock_internet=false, unblock_screen=false) desde el panel.
--
--  Contrato FIJO (el cliente C# lee estos nombres igual):
--    pc_name TEXT PRIMARY KEY, unblock_internet BOOLEAN NOT NULL
--    DEFAULT false, unblock_screen BOOLEAN NOT NULL DEFAULT false,
--    updated_at TIMESTAMPTZ DEFAULT NOW(), updated_by TEXT.
--
--  Idempotente. Correr en Supabase SQL Editor.
-- ============================================================

-- ============================================================
--  1. Tabla pc_overrides
--  Una fila por PC (pc_name como PK). Las dos flags nacen en false:
--  una fila sin override no desbloquea nada (el PC sigue bajo las
--  reglas globales / por-evaluacion). El profe las pone en true para
--  liberar ESE PC.
-- ============================================================

CREATE TABLE IF NOT EXISTS public.pc_overrides (
  pc_name TEXT PRIMARY KEY,
  unblock_internet BOOLEAN NOT NULL DEFAULT false,
  unblock_screen BOOLEAN NOT NULL DEFAULT false,
  updated_at TIMESTAMPTZ DEFAULT NOW(),
  updated_by TEXT
);

-- ============================================================
--  2. ROW LEVEL SECURITY
--  Mismo patron que el resto del control: anon + authenticated LEEN
--  (el cliente pollea su override por pc_name), authenticated ALL
--  escribe (el panel hace upsert/update). Idempotente via
--  DROP POLICY IF EXISTS + CREATE.
-- ============================================================

ALTER TABLE public.pc_overrides ENABLE ROW LEVEL SECURITY;

-- SELECT para anon (cliente) y authenticated (panel).
DROP POLICY IF EXISTS "anon_read_pc_overrides" ON public.pc_overrides;
CREATE POLICY "anon_read_pc_overrides" ON public.pc_overrides
  FOR SELECT TO anon, authenticated USING (true);

-- ALL (INSERT/UPDATE/DELETE) solo para el panel autenticado.
DROP POLICY IF EXISTS "auth_all_pc_overrides" ON public.pc_overrides;
CREATE POLICY "auth_all_pc_overrides" ON public.pc_overrides
  FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- ============================================================
--  3. REALTIME
--  Publica pc_overrides para que el cliente reciba en vivo el
--  desbloqueo (o la reactivacion de monitoreo) de su PC sin esperar
--  al siguiente poll. DO-block guardado: no falla si ya estaba en la
--  publicacion.
-- ============================================================

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_publication_tables
    WHERE pubname = 'supabase_realtime'
      AND schemaname = 'public'
      AND tablename = 'pc_overrides'
  ) THEN
    EXECUTE 'ALTER PUBLICATION supabase_realtime ADD TABLE public.pc_overrides';
    RAISE NOTICE 'Realtime habilitado para public.pc_overrides';
  ELSE
    RAISE NOTICE 'public.pc_overrides ya estaba en supabase_realtime (sin cambios)';
  END IF;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

-- ============================================================
--  4. VERIFICACION
-- ============================================================

SELECT 'pc_overrides (total)' AS check, COUNT(*) AS n
  FROM public.pc_overrides
UNION ALL
SELECT 'pc_overrides desbloqueados', COUNT(*)
  FROM public.pc_overrides
  WHERE unblock_internet OR unblock_screen;
