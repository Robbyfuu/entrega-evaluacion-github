-- ============================================================
--  SETUP COMPLETO Supabase - Entrega de Evaluacion a GitHub
--  Ejecutar TODO de una vez en SQL Editor. Es idempotente:
--  podes re-ejecutarlo sin romper nada.
-- ============================================================

-- ============================================================
--  1. TABLAS
-- ============================================================

-- Control admin (single row id=1)
CREATE TABLE IF NOT EXISTS public.control (
  id INT PRIMARY KEY DEFAULT 1 CHECK (id = 1),
  internet_block BOOLEAN NOT NULL DEFAULT false,
  force_lockdown BOOLEAN NOT NULL DEFAULT false,
  message TEXT NOT NULL DEFAULT '',
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_by TEXT
);
INSERT INTO public.control (id) VALUES (1) ON CONFLICT (id) DO NOTHING;

-- Eventos de trampa (repo con archivos pre-existentes)
CREATE TABLE IF NOT EXISTS public.cheat_events (
  id BIGSERIAL PRIMARY KEY,
  username TEXT,
  pc_name TEXT,
  repo_name TEXT,
  files_count INT,
  files_sample TEXT[],
  detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Actividad de alumnos (login, create_repo, clone, upload)
CREATE TABLE IF NOT EXISTS public.student_activity (
  id BIGSERIAL PRIMARY KEY,
  github_username TEXT NOT NULL,
  github_email TEXT,
  pc_name TEXT,
  action TEXT NOT NULL CHECK (action IN ('login','create_repo','upload','clone')),
  repo_name TEXT,
  repo_url TEXT,
  section TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE public.student_activity ADD COLUMN IF NOT EXISTS section TEXT;
CREATE INDEX IF NOT EXISTS idx_activity_user_action
  ON public.student_activity (github_username, action, created_at DESC);

-- Tareas de GitHub Classroom
CREATE TABLE IF NOT EXISTS public.assignments (
  id BIGSERIAL PRIMARY KEY,
  title TEXT NOT NULL,
  classroom_url TEXT NOT NULL,
  section TEXT NOT NULL DEFAULT '',
  org TEXT NOT NULL DEFAULT '',
  active BOOLEAN NOT NULL DEFAULT true,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  created_by TEXT
);
ALTER TABLE public.assignments ADD COLUMN IF NOT EXISTS section TEXT NOT NULL DEFAULT '';
ALTER TABLE public.assignments ADD COLUMN IF NOT EXISTS org TEXT NOT NULL DEFAULT '';

-- PCs conectados (heartbeat)
CREATE TABLE IF NOT EXISTS public.online_clients (
  id BIGSERIAL PRIMARY KEY,
  pc_name TEXT NOT NULL,
  github_username TEXT,
  github_email TEXT,
  section TEXT,
  processes JSONB DEFAULT '[]'::jsonb,
  internet_state TEXT DEFAULT 'free',
  lockdown_state TEXT DEFAULT 'none',
  last_seen TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(pc_name, github_username)
);
ALTER TABLE public.online_clients ADD COLUMN IF NOT EXISTS processes JSONB DEFAULT '[]'::jsonb;
ALTER TABLE public.online_clients ADD COLUMN IF NOT EXISTS internet_state TEXT DEFAULT 'free';
ALTER TABLE public.online_clients ADD COLUMN IF NOT EXISTS lockdown_state TEXT DEFAULT 'none';
CREATE INDEX IF NOT EXISTS idx_online_last_seen
  ON public.online_clients (last_seen DESC);

-- Alertas de procesos sospechosos
CREATE TABLE IF NOT EXISTS public.process_alerts (
  id BIGSERIAL PRIMARY KEY,
  pc_name TEXT NOT NULL,
  github_username TEXT,
  section TEXT,
  process_name TEXT NOT NULL,
  window_title TEXT,
  detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_alerts_detected
  ON public.process_alerts (detected_at DESC);

-- Procesos sospechosos editables por seccion (blocklist)
-- section IS NULL = regla GLOBAL; section = 'X' = extra de la seccion X.
-- process_name se guarda NORMALIZADO (lowercase, sin .exe, trim).
CREATE TABLE IF NOT EXISTS public.suspicious_processes (
  id BIGSERIAL PRIMARY KEY,
  process_name TEXT NOT NULL,
  section TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_susproc_name_section
  ON public.suspicious_processes (process_name, COALESCE(section, ''));
CREATE INDEX IF NOT EXISTS idx_susproc_section
  ON public.suspicious_processes (section);

-- Lockdown dirigido por PC
CREATE TABLE IF NOT EXISTS public.targeted_lockdowns (
  id BIGSERIAL PRIMARY KEY,
  pc_name TEXT NOT NULL,
  github_username TEXT NOT NULL,
  active BOOLEAN NOT NULL DEFAULT true,
  reason TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  released_at TIMESTAMPTZ,
  UNIQUE(pc_name, github_username)
);

-- ============================================================
--  2. ROW LEVEL SECURITY
-- ============================================================
ALTER TABLE public.control ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.cheat_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.student_activity ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.online_clients ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.process_alerts ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.targeted_lockdowns ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.suspicious_processes ENABLE ROW LEVEL SECURITY;

-- ============================================================
--  3. POLICIES
-- ============================================================

-- control: anon lee, authenticated actualiza
DROP POLICY IF EXISTS "anon_read_control" ON public.control;
CREATE POLICY "anon_read_control" ON public.control
  FOR SELECT USING (true);
DROP POLICY IF EXISTS "auth_update_control" ON public.control;
CREATE POLICY "auth_update_control" ON public.control
  FOR UPDATE TO authenticated USING (true) WITH CHECK (true);

-- cheat_events: anon inserta, authenticated lee
DROP POLICY IF EXISTS "anon_insert_cheat" ON public.cheat_events;
CREATE POLICY "anon_insert_cheat" ON public.cheat_events
  FOR INSERT WITH CHECK (true);
DROP POLICY IF EXISTS "auth_read_cheat" ON public.cheat_events;
CREATE POLICY "auth_read_cheat" ON public.cheat_events
  FOR SELECT TO authenticated USING (true);

-- student_activity: anon inserta, authenticated lee
DROP POLICY IF EXISTS "anon_insert_activity" ON public.student_activity;
CREATE POLICY "anon_insert_activity" ON public.student_activity
  FOR INSERT WITH CHECK (true);
DROP POLICY IF EXISTS "auth_read_activity" ON public.student_activity;
CREATE POLICY "auth_read_activity" ON public.student_activity
  FOR SELECT TO authenticated USING (true);

-- assignments: anon lee activos, authenticated CRUD
DROP POLICY IF EXISTS "anon_read_assignments" ON public.assignments;
CREATE POLICY "anon_read_assignments" ON public.assignments
  FOR SELECT USING (active = true);
DROP POLICY IF EXISTS "auth_all_assignments" ON public.assignments;
CREATE POLICY "auth_all_assignments" ON public.assignments
  FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- online_clients: solo authenticated lee. Escritura via RPC SECURITY DEFINER.
DROP POLICY IF EXISTS "anon_insert_online" ON public.online_clients;
DROP POLICY IF EXISTS "anon_update_online" ON public.online_clients;
DROP POLICY IF EXISTS "anon_read_online" ON public.online_clients;
DROP POLICY IF EXISTS "auth_read_online" ON public.online_clients;
CREATE POLICY "auth_read_online" ON public.online_clients
  FOR SELECT TO authenticated USING (true);

-- process_alerts: authenticated lee. La insercion del cliente pasa por
-- la RPC report_process_alert (SECURITY DEFINER, ver seccion 4). Aca (setup
-- canonico = estado final, mundo post-PR2 con el cliente ya usando la RPC)
-- NO se crea la policy anon_insert_alerts: el INSERT directo de anon queda
-- deshabilitado a proposito.
--
-- OJO sobre una DB EXISTENTE con clientes v2.5.0 todavia activos: NO borres
-- anon_insert_alerts hasta que PR2 (cliente -> RPC) este desplegado en todas
-- las maquinas; si no, las alertas desaparecen en silencio. Ver la nota de
-- secuencia en migration-blocklist.sql seccion 5.
-- ROLLBACK del hardening (revertir cliente a INSERT directo):
--   CREATE POLICY "anon_insert_alerts" ON public.process_alerts
--     FOR INSERT WITH CHECK (true);
DROP POLICY IF EXISTS "anon_insert_alerts" ON public.process_alerts;
DROP POLICY IF EXISTS "auth_read_alerts" ON public.process_alerts;
CREATE POLICY "auth_read_alerts" ON public.process_alerts
  FOR SELECT TO authenticated USING (true);

-- suspicious_processes: anon + authenticated leen; solo authenticated escribe
DROP POLICY IF EXISTS "anon_read_susproc" ON public.suspicious_processes;
CREATE POLICY "anon_read_susproc" ON public.suspicious_processes
  FOR SELECT TO anon, authenticated USING (true);
DROP POLICY IF EXISTS "auth_all_susproc" ON public.suspicious_processes;
CREATE POLICY "auth_all_susproc" ON public.suspicious_processes
  FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- targeted_lockdowns: anon lee (cliente chequea el suyo), authenticated CRUD
DROP POLICY IF EXISTS "anon_read_targeted" ON public.targeted_lockdowns;
CREATE POLICY "anon_read_targeted" ON public.targeted_lockdowns
  FOR SELECT TO anon, authenticated USING (true);
DROP POLICY IF EXISTS "auth_all_targeted" ON public.targeted_lockdowns;
CREATE POLICY "auth_all_targeted" ON public.targeted_lockdowns
  FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- ============================================================
--  4. RPC heartbeat (SECURITY DEFINER - bypasea RLS de online_clients)
-- ============================================================
CREATE OR REPLACE FUNCTION public.heartbeat(
  p_pc_name TEXT,
  p_github_username TEXT,
  p_github_email TEXT DEFAULT NULL,
  p_section TEXT DEFAULT NULL,
  p_processes JSONB DEFAULT '[]'::jsonb,
  p_internet_state TEXT DEFAULT 'free',
  p_lockdown_state TEXT DEFAULT 'none'
) RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
  INSERT INTO public.online_clients
    (pc_name, github_username, github_email, section, processes,
     internet_state, lockdown_state, last_seen)
  VALUES
    (p_pc_name, p_github_username, p_github_email, p_section, p_processes,
     p_internet_state, p_lockdown_state, NOW())
  ON CONFLICT (pc_name, github_username) DO UPDATE
  SET github_email   = EXCLUDED.github_email,
      section        = EXCLUDED.section,
      processes      = EXCLUDED.processes,
      internet_state = EXCLUDED.internet_state,
      lockdown_state = EXCLUDED.lockdown_state,
      last_seen      = NOW();
END;
$$;

GRANT EXECUTE ON FUNCTION public.heartbeat(TEXT,TEXT,TEXT,TEXT,JSONB,TEXT,TEXT)
  TO anon, authenticated;

-- RPC report_process_alert (SECURITY DEFINER, rate-limit 30s).
-- El cliente inserta alertas via esta RPC en vez de INSERT directo:
-- descarta duplicados (mismo pc_name + process_name) dentro de 30s.
CREATE OR REPLACE FUNCTION public.report_process_alert(
  p_github_username TEXT,
  p_pc_name TEXT,
  p_section TEXT,
  p_process_name TEXT,
  p_window_title TEXT
) RETURNS VOID
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM public.process_alerts
    WHERE pc_name = p_pc_name
      AND process_name = p_process_name
      AND detected_at > NOW() - INTERVAL '30 seconds'
  ) THEN
    RETURN;
  END IF;

  INSERT INTO public.process_alerts
    (pc_name, github_username, section, process_name, window_title, detected_at)
  VALUES
    (p_pc_name, p_github_username, p_section, p_process_name, p_window_title, NOW());
END;
$$;
GRANT EXECUTE ON FUNCTION public.report_process_alert(TEXT,TEXT,TEXT,TEXT,TEXT)
  TO anon, authenticated;

-- ============================================================
--  4b. SEED blocklist global (section = NULL) + realtime
-- ============================================================
-- Reglas globales heredadas por todas las secciones. Copiado de
-- Config.SuspiciousProcesses (ya normalizado). Idempotente.
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

-- Realtime: agregar suspicious_processes a la publicacion (guardado).
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_publication_tables
    WHERE pubname = 'supabase_realtime'
      AND schemaname = 'public'
      AND tablename = 'suspicious_processes'
  ) THEN
    EXECUTE 'ALTER PUBLICATION supabase_realtime ADD TABLE public.suspicious_processes';
  END IF;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

-- ============================================================
--  5. TRIGGER updated_at en control
-- ============================================================
CREATE OR REPLACE FUNCTION public.set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at = NOW();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_control_updated_at ON public.control;
CREATE TRIGGER trg_control_updated_at
  BEFORE UPDATE ON public.control
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- ============================================================
--  6. VERIFICACION
-- ============================================================
SELECT 'control' AS tabla, COUNT(*) FROM public.control
UNION ALL SELECT 'assignments', COUNT(*) FROM public.assignments
UNION ALL SELECT 'online_clients', COUNT(*) FROM public.online_clients
UNION ALL SELECT 'targeted_lockdowns', COUNT(*) FROM public.targeted_lockdowns
UNION ALL SELECT 'process_alerts', COUNT(*) FROM public.process_alerts
UNION ALL SELECT 'cheat_events', COUNT(*) FROM public.cheat_events
UNION ALL SELECT 'student_activity', COUNT(*) FROM public.student_activity
UNION ALL SELECT 'suspicious_processes', COUNT(*) FROM public.suspicious_processes;
