-- ============================================================
--  Migracion: suspicious_processes (blocklist editable por seccion)
--  + endurecimiento de process_alerts via RPC con rate-limit.
--  Idempotente. Correr en Supabase SQL Editor.
--
--  Modelo: section IS NULL = regla GLOBAL (heredada por todas las
--  secciones); section = 'X' = regla extra de la seccion X. La lista
--  efectiva de un alumno de la seccion X es (section IS NULL) UNION
--  (section = 'X'). process_name se guarda NORMALIZADO: lowercase,
--  sin sufijo .exe, trim (igual que Config.NormalizeProcessName en C#).
-- ============================================================

-- ============================================================
--  1. TABLA suspicious_processes
-- ============================================================
CREATE TABLE IF NOT EXISTS public.suspicious_processes (
  id BIGSERIAL PRIMARY KEY,
  process_name TEXT NOT NULL,
  section TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Unicidad por (process_name, section) tratando NULL como '' para que
-- el seed y los inserts futuros sean idempotentes (ON CONFLICT).
CREATE UNIQUE INDEX IF NOT EXISTS ux_susproc_name_section
  ON public.suspicious_processes (process_name, COALESCE(section, ''));

-- Indice de apoyo para la query del cliente (filtra por seccion).
CREATE INDEX IF NOT EXISTS idx_susproc_section
  ON public.suspicious_processes (section);

-- ============================================================
--  2. ROW LEVEL SECURITY
-- ============================================================
ALTER TABLE public.suspicious_processes ENABLE ROW LEVEL SECURITY;

-- anon + authenticated leen; solo authenticated escribe (CRUD del profe).
DROP POLICY IF EXISTS "anon_read_susproc" ON public.suspicious_processes;
CREATE POLICY "anon_read_susproc" ON public.suspicious_processes
  FOR SELECT TO anon, authenticated USING (true);

DROP POLICY IF EXISTS "auth_all_susproc" ON public.suspicious_processes;
CREATE POLICY "auth_all_susproc" ON public.suspicious_processes
  FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- ============================================================
--  3. SEED de las reglas globales (section = NULL)
--  Copiado de Config.SuspiciousProcesses (ya normalizado).
--  Idempotente: ON CONFLICT contra ux_susproc_name_section.
-- ============================================================
INSERT INTO public.suspicious_processes (process_name, section)
SELECT name, NULL
FROM unnest(ARRAY[
  'chrome','msedge','firefox','opera','brave','iexplore','vivaldi','tor',
  'whatsapp','discord','telegram','slack','teams','skype',
  'notion','obsidian','evernote','onenote','winword','excel',
  'code','pycharm','pycharm64','sublime_text','notepad','notepad++','devenv',
  'anydesk','teamviewer','rustdesk','msrdc',
  'chatgpt','claude','copilot'
]) AS name
ON CONFLICT (process_name, COALESCE(section, '')) DO NOTHING;

-- ============================================================
--  4. RPC report_process_alert (SECURITY DEFINER, rate-limit 30s)
--  Endurece process_alerts: el cliente ya no inserta directo, llama
--  a esta RPC server-side. Mirror de heartbeat / record_acceptance.
--  Rate-limit: si existe un alert identico (mismo pc_name +
--  process_name) en los ultimos 30 segundos, NO inserta (anti-flood).
--  p_evaluation_id es nullable (DEFAULT NULL): los clientes viejos que
--  llaman con la firma de 5 args siguen resolviendo y el comportamiento
--  con NULL es identico al de hoy. DROP previo de la firma vieja (5
--  params) antes del CREATE para evitar el overload (PostgREST 300):
--  exactamente UNA firma por nombre. Espejo de la definicion en
--  setup-supabase.sql; ambas convergen a la misma firma para que
--  re-correr cualquiera de los dos archivos no resucite el overload.
-- ============================================================
DROP FUNCTION IF EXISTS public.report_process_alert(
  TEXT, TEXT, TEXT, TEXT, TEXT);

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
--  5. (DIFERIDO) Quitar el INSERT directo de anon sobre process_alerts
--
--  NO se quita en esta migracion A PROPOSITO. El cliente en produccion
--  (v2.5.0) todavia inserta DIRECTO en process_alerts y se traga los
--  errores en un catch silencioso. Si borraramos la policy ahora, cada
--  PC de examen que aun no se actualizo dejaria de registrar alertas SIN
--  ningun aviso, hasta que PR2 (cliente C# -> RPC) este desplegado en
--  todas las maquinas.
--
--  La RPC report_process_alert (arriba) y el INSERT directo de anon
--  COEXISTEN sin problema: el cliente nuevo usa la RPC, el viejo sigue
--  insertando directo. Esta migracion es forward-compatible.
--
--  SECUENCIA correcta:
--    1. correr esta migracion (crea tabla + RPC, no rompe nada).
--    2. PR2: cliente C# pasa a llamar la RPC. Buildear + desplegar a TODOS
--       los PC de examen (auto-update Velopack).
--    3. confirmar que ningun cliente v2.5.0 sigue activo.
--    4. recien ahi correr migration-blocklist-drop-anon-insert.sql:
--         DROP POLICY IF EXISTS "anon_insert_alerts" ON public.process_alerts;
-- ============================================================
-- (sin cambios sobre process_alerts en esta migracion)

-- ============================================================
--  6. Realtime: agregar suspicious_processes a la publicacion.
--  Guardado contra duplicate_object para que re-correr no falle.
-- ============================================================
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_publication_tables
    WHERE pubname = 'supabase_realtime'
      AND schemaname = 'public'
      AND tablename = 'suspicious_processes'
  ) THEN
    EXECUTE 'ALTER PUBLICATION supabase_realtime ADD TABLE public.suspicious_processes';
    RAISE NOTICE 'Realtime habilitado para public.suspicious_processes';
  ELSE
    RAISE NOTICE 'public.suspicious_processes ya estaba en supabase_realtime (sin cambios)';
  END IF;
EXCEPTION
  WHEN duplicate_object THEN
    RAISE NOTICE 'public.suspicious_processes ya estaba en la publicacion (duplicate_object)';
END $$;

-- ============================================================
--  7. VERIFICACION
-- ============================================================
SELECT 'suspicious_processes' AS tabla, COUNT(*) AS filas FROM public.suspicious_processes
UNION ALL
SELECT 'susproc_globales (section NULL)', COUNT(*) FROM public.suspicious_processes WHERE section IS NULL;
