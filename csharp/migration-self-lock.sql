-- ============================================================
--  Migracion: auto-lock por trampa local visible + liberable remoto
--
--  Hoy la "pantalla roja" por trampa LOCAL (repo sucio, navegacion
--  prohibida, copilot) NO es visible en el panel ni liberable remoto:
--  solo la clave del profe en la maquina la cierra. Esta migracion deja
--  que el cliente (anon) REPORTE su propio bloqueo en targeted_lockdowns
--  via una RPC SECURITY DEFINER, para que el profe lo VEA y lo
--  DESBLOQUEE desde el panel (la liberacion sigue siendo authenticated:
--  el alumno NO puede auto-liberarse).
--  Idempotente. Correr en Supabase SQL Editor.
-- ============================================================

-- 1. Distinguir origen del lock: 'teacher' (dirigido por el profe) vs
--    'trap' (auto-reportado por una trampa local del cliente).
ALTER TABLE public.targeted_lockdowns
  ADD COLUMN IF NOT EXISTS source TEXT NOT NULL DEFAULT 'teacher'
  CHECK (source IN ('teacher', 'trap'));

-- 2. RPC para que el cliente reporte su propio bloqueo (anon-callable).
--    SECURITY DEFINER: escribe aunque el cliente sea anon (la RLS solo deja
--    escribir a authenticated). Reportarse bloqueado NO es explotable; la
--    LIBERACION queda fuera de esta RPC (solo el profe authenticated apaga
--    active desde el panel). UNA firma por nombre (DROP previo).
DROP FUNCTION IF EXISTS public.report_self_lock(TEXT, TEXT, TEXT, TEXT, TEXT);

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

-- 3. Verificacion
SELECT 'targeted_lockdowns activos' AS check, COUNT(*) AS n
FROM public.targeted_lockdowns WHERE active
UNION ALL
SELECT 'por trampa (source=trap)', COUNT(*)
FROM public.targeted_lockdowns WHERE active AND source = 'trap';
