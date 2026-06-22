-- ============================================================
--  MIGRACIÓN COMBINADA — Entrega de Evaluación a GitHub
--  Correr TODO de una vez en Supabase SQL Editor.
--  Es 100% idempotente: podés re-ejecutarlo sin romper nada.
--
--  Orden:
--    1. setup-supabase (tablas base + RPCs + RLS)
--    2. migration-browser (browser_history)
--    3. migration-acceptances (assignment_acceptances + RPC)
--    4. migration-multi-evaluation (courses > sections > evaluations)
--    5. migration-submissions (assignment_submissions + allows_manual_submission)
--    6. migration-realtime (publicaciones Realtime)
-- ============================================================

-- ============================================================
--  1. SETUP BASE — tablas, RLS, policies, RPCs, seed
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

-- Eventos de trampa
CREATE TABLE IF NOT EXISTS public.cheat_events (
  id BIGSERIAL PRIMARY KEY,
  username TEXT,
  pc_name TEXT,
  repo_name TEXT,
  files_count INT,
  files_sample TEXT[],
  detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Actividad de alumnos
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

-- Tareas (assignments)
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
CREATE INDEX IF NOT EXISTS idx_online_last_seen ON public.online_clients (last_seen DESC);

-- Alertas de procesos
CREATE TABLE IF NOT EXISTS public.process_alerts (
  id BIGSERIAL PRIMARY KEY,
  pc_name TEXT NOT NULL,
  github_username TEXT,
  section TEXT,
  process_name TEXT NOT NULL,
  window_title TEXT,
  detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_alerts_detected ON public.process_alerts (detected_at DESC);

-- Blocklist de procesos sospechosos
CREATE TABLE IF NOT EXISTS public.suspicious_processes (
  id BIGSERIAL PRIMARY KEY,
  process_name TEXT NOT NULL,
  section TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_susproc_name_section
  ON public.suspicious_processes (process_name, COALESCE(section, ''));
CREATE INDEX IF NOT EXISTS idx_susproc_section ON public.suspicious_processes (section);

-- Lockdown dirigido
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
--  1b. RLS + POLICIES (tablas base)
-- ============================================================
ALTER TABLE public.control ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.cheat_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.student_activity ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.online_clients ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.process_alerts ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.targeted_lockdowns ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.suspicious_processes ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "anon_read_control" ON public.control;
CREATE POLICY "anon_read_control" ON public.control FOR SELECT USING (true);
DROP POLICY IF EXISTS "auth_update_control" ON public.control;
CREATE POLICY "auth_update_control" ON public.control FOR UPDATE TO authenticated USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS "anon_insert_cheat" ON public.cheat_events;
CREATE POLICY "anon_insert_cheat" ON public.cheat_events FOR INSERT WITH CHECK (true);
DROP POLICY IF EXISTS "auth_read_cheat" ON public.cheat_events;
CREATE POLICY "auth_read_cheat" ON public.cheat_events FOR SELECT TO authenticated USING (true);

DROP POLICY IF EXISTS "anon_insert_activity" ON public.student_activity;
CREATE POLICY "anon_insert_activity" ON public.student_activity FOR INSERT WITH CHECK (true);
DROP POLICY IF EXISTS "auth_read_activity" ON public.student_activity;
CREATE POLICY "auth_read_activity" ON public.student_activity FOR SELECT TO authenticated USING (true);

DROP POLICY IF EXISTS "anon_read_assignments" ON public.assignments;
CREATE POLICY "anon_read_assignments" ON public.assignments FOR SELECT USING (active = true);
DROP POLICY IF EXISTS "auth_all_assignments" ON public.assignments;
CREATE POLICY "auth_all_assignments" ON public.assignments FOR ALL TO authenticated USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS "anon_insert_online" ON public.online_clients;
DROP POLICY IF EXISTS "anon_update_online" ON public.online_clients;
DROP POLICY IF EXISTS "anon_read_online" ON public.online_clients;
DROP POLICY IF EXISTS "auth_read_online" ON public.online_clients;
CREATE POLICY "auth_read_online" ON public.online_clients FOR SELECT TO authenticated USING (true);

DROP POLICY IF EXISTS "anon_insert_alerts" ON public.process_alerts;
DROP POLICY IF EXISTS "auth_read_alerts" ON public.process_alerts;
CREATE POLICY "auth_read_alerts" ON public.process_alerts FOR SELECT TO authenticated USING (true);

DROP POLICY IF EXISTS "anon_read_susproc" ON public.suspicious_processes;
CREATE POLICY "anon_read_susproc" ON public.suspicious_processes FOR SELECT TO anon, authenticated USING (true);
DROP POLICY IF EXISTS "auth_all_susproc" ON public.suspicious_processes;
CREATE POLICY "auth_all_susproc" ON public.suspicious_processes FOR ALL TO authenticated USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS "anon_read_targeted" ON public.targeted_lockdowns;
CREATE POLICY "anon_read_targeted" ON public.targeted_lockdowns FOR SELECT TO anon, authenticated USING (true);
DROP POLICY IF EXISTS "auth_all_targeted" ON public.targeted_lockdowns;
CREATE POLICY "auth_all_targeted" ON public.targeted_lockdowns FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- ============================================================
--  1c. RPCs (heartbeat + process_alert)
-- ============================================================
CREATE OR REPLACE FUNCTION public.heartbeat(
  p_pc_name TEXT, p_github_username TEXT, p_github_email TEXT DEFAULT NULL,
  p_section TEXT DEFAULT NULL, p_processes JSONB DEFAULT '[]'::jsonb,
  p_internet_state TEXT DEFAULT 'free', p_lockdown_state TEXT DEFAULT 'none'
) RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  INSERT INTO public.online_clients
    (pc_name, github_username, github_email, section, processes, internet_state, lockdown_state, last_seen)
  VALUES
    (p_pc_name, p_github_username, p_github_email, p_section, p_processes, p_internet_state, p_lockdown_state, NOW())
  ON CONFLICT (pc_name, github_username) DO UPDATE
  SET github_email = EXCLUDED.github_email, section = EXCLUDED.section,
      processes = EXCLUDED.processes, internet_state = EXCLUDED.internet_state,
      lockdown_state = EXCLUDED.lockdown_state, last_seen = NOW();
END; $$;
GRANT EXECUTE ON FUNCTION public.heartbeat(TEXT,TEXT,TEXT,TEXT,JSONB,TEXT,TEXT) TO anon, authenticated;

CREATE OR REPLACE FUNCTION public.report_process_alert(
  p_github_username TEXT, p_pc_name TEXT, p_section TEXT,
  p_process_name TEXT, p_window_title TEXT
) RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM public.process_alerts
    WHERE pc_name = p_pc_name AND process_name = p_process_name
      AND detected_at > NOW() - INTERVAL '30 seconds'
  ) THEN RETURN; END IF;
  INSERT INTO public.process_alerts (pc_name, github_username, section, process_name, window_title, detected_at)
  VALUES (p_pc_name, p_github_username, p_section, p_process_name, p_window_title, NOW());
END; $$;
GRANT EXECUTE ON FUNCTION public.report_process_alert(TEXT,TEXT,TEXT,TEXT,TEXT) TO anon, authenticated;

-- Seed blocklist global
INSERT INTO public.suspicious_processes (process_name, section)
SELECT name, NULL FROM unnest(ARRAY[
  'chrome','msedge','firefox','opera','brave','iexplore','vivaldi','tor',
  'whatsapp','discord','telegram','slack','teams','skype',
  'notion','obsidian','evernote','onenote','winword','excel',
  'code','pycharm','pycharm64','sublime_text','notepad','notepad++','devenv',
  'anydesk','teamviewer','rustdesk','msrdc','chatgpt','claude','copilot'
]) AS name
ON CONFLICT (process_name, COALESCE(section, '')) DO NOTHING;

-- Trigger updated_at en control
CREATE OR REPLACE FUNCTION public.set_updated_at() RETURNS TRIGGER AS $$
BEGIN NEW.updated_at = NOW(); RETURN NEW; END; $$ LANGUAGE plpgsql;
DROP TRIGGER IF EXISTS trg_control_updated_at ON public.control;
CREATE TRIGGER trg_control_updated_at BEFORE UPDATE ON public.control
  FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- ============================================================
--  2. BROWSER HISTORY
-- ============================================================
CREATE TABLE IF NOT EXISTS public.browser_history (
  id BIGSERIAL PRIMARY KEY,
  github_username TEXT, pc_name TEXT, section TEXT,
  url TEXT NOT NULL, domain TEXT,
  allowed BOOLEAN NOT NULL DEFAULT true,
  visited_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_browser_user ON public.browser_history (github_username, visited_at DESC);
CREATE INDEX IF NOT EXISTS idx_browser_blocked ON public.browser_history (allowed, visited_at DESC);
ALTER TABLE public.browser_history ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "anon_insert_browser" ON public.browser_history;
CREATE POLICY "anon_insert_browser" ON public.browser_history FOR INSERT WITH CHECK (true);
DROP POLICY IF EXISTS "auth_read_browser" ON public.browser_history;
CREATE POLICY "auth_read_browser" ON public.browser_history FOR SELECT TO authenticated USING (true);

-- ============================================================
--  3. ASSIGNMENT ACCEPTANCES
-- ============================================================
CREATE TABLE IF NOT EXISTS public.assignment_acceptances (
  id BIGSERIAL PRIMARY KEY,
  github_username TEXT NOT NULL,
  assignment_id BIGINT,
  assignment_title TEXT,
  section TEXT,
  repo_name TEXT,
  repo_url TEXT,
  accepted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(github_username, assignment_id)
);
CREATE INDEX IF NOT EXISTS idx_acceptances_user
  ON public.assignment_acceptances (github_username, accepted_at DESC);
ALTER TABLE public.assignment_acceptances ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "anon_insert_acceptances" ON public.assignment_acceptances;
CREATE POLICY "anon_insert_acceptances" ON public.assignment_acceptances FOR INSERT WITH CHECK (true);
DROP POLICY IF EXISTS "anon_read_acceptances" ON public.assignment_acceptances;
CREATE POLICY "anon_read_acceptances" ON public.assignment_acceptances FOR SELECT TO anon, authenticated USING (true);
DROP POLICY IF EXISTS "anon_update_acceptances" ON public.assignment_acceptances;
CREATE POLICY "anon_update_acceptances" ON public.assignment_acceptances FOR UPDATE USING (true) WITH CHECK (true);

DROP FUNCTION IF EXISTS public.record_acceptance(TEXT, BIGINT, TEXT, TEXT, TEXT, TEXT);
CREATE OR REPLACE FUNCTION public.record_acceptance(
  p_github_username TEXT, p_assignment_id BIGINT,
  p_assignment_title TEXT DEFAULT NULL, p_section TEXT DEFAULT NULL,
  p_repo_name TEXT DEFAULT NULL, p_repo_url TEXT DEFAULT NULL,
  p_evaluation_id BIGINT DEFAULT NULL
) RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  INSERT INTO public.assignment_acceptances
    (github_username, assignment_id, assignment_title, section, repo_name, repo_url, evaluation_id, accepted_at)
  VALUES
    (p_github_username, p_assignment_id, p_assignment_title, p_section, p_repo_name, p_repo_url, p_evaluation_id, NOW())
  ON CONFLICT (github_username, assignment_id) DO UPDATE
  SET assignment_title = EXCLUDED.assignment_title, section = EXCLUDED.section,
      repo_name = EXCLUDED.repo_name, repo_url = EXCLUDED.repo_url,
      evaluation_id = COALESCE(EXCLUDED.evaluation_id, public.assignment_acceptances.evaluation_id),
      accepted_at = NOW();
END; $$;
GRANT EXECUTE ON FUNCTION public.record_acceptance(TEXT,BIGINT,TEXT,TEXT,TEXT,TEXT,BIGINT) TO anon, authenticated;

-- ============================================================
--  4. MULTI-EVALUACION (Curso > Seccion > Evaluacion)
-- ============================================================
CREATE TABLE IF NOT EXISTS public.courses (
  id BIGSERIAL PRIMARY KEY, code TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL, active BOOLEAN NOT NULL DEFAULT true,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS public.sections (
  id BIGSERIAL PRIMARY KEY, course_id BIGINT NOT NULL REFERENCES public.courses(id) ON DELETE CASCADE,
  code TEXT NOT NULL, name TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), UNIQUE(course_id, code)
);
CREATE TABLE IF NOT EXISTS public.evaluations (
  id BIGSERIAL PRIMARY KEY, section_id BIGINT NOT NULL REFERENCES public.sections(id) ON DELETE CASCADE,
  title TEXT NOT NULL, classroom_url TEXT, org TEXT,
  active BOOLEAN NOT NULL DEFAULT false,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), UNIQUE(section_id, title)
);
CREATE INDEX IF NOT EXISTS idx_sections_course ON public.sections (course_id);
CREATE INDEX IF NOT EXISTS idx_evaluations_section ON public.evaluations (section_id);
CREATE INDEX IF NOT EXISTS idx_evaluations_active ON public.evaluations (active);

ALTER TABLE public.courses ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.sections ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.evaluations ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "anon_read_courses" ON public.courses;
CREATE POLICY "anon_read_courses" ON public.courses FOR SELECT USING (true);
DROP POLICY IF EXISTS "auth_all_courses" ON public.courses;
CREATE POLICY "auth_all_courses" ON public.courses FOR ALL TO authenticated USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS "anon_read_sections" ON public.sections;
CREATE POLICY "anon_read_sections" ON public.sections FOR SELECT USING (true);
DROP POLICY IF EXISTS "auth_all_sections" ON public.sections;
CREATE POLICY "auth_all_sections" ON public.sections FOR ALL TO authenticated USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS "anon_read_evaluations" ON public.evaluations;
CREATE POLICY "anon_read_evaluations" ON public.evaluations FOR SELECT USING (active = true);
DROP POLICY IF EXISTS "auth_all_evaluations" ON public.evaluations;
CREATE POLICY "auth_all_evaluations" ON public.evaluations FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- FK: assignments + 6 tablas con section_id
ALTER TABLE public.assignments ADD COLUMN IF NOT EXISTS evaluation_id BIGINT NULL REFERENCES public.evaluations(id) ON DELETE SET NULL;
ALTER TABLE public.assignment_acceptances ADD COLUMN IF NOT EXISTS evaluation_id BIGINT NULL REFERENCES public.evaluations(id) ON DELETE SET NULL;
ALTER TABLE public.assignment_acceptances ADD COLUMN IF NOT EXISTS section_id BIGINT NULL REFERENCES public.sections(id) ON DELETE SET NULL;
ALTER TABLE public.student_activity ADD COLUMN IF NOT EXISTS section_id BIGINT NULL REFERENCES public.sections(id) ON DELETE SET NULL;
ALTER TABLE public.online_clients ADD COLUMN IF NOT EXISTS section_id BIGINT NULL REFERENCES public.sections(id) ON DELETE SET NULL;
ALTER TABLE public.process_alerts ADD COLUMN IF NOT EXISTS section_id BIGINT NULL REFERENCES public.sections(id) ON DELETE SET NULL;
ALTER TABLE public.browser_history ADD COLUMN IF NOT EXISTS section_id BIGINT NULL REFERENCES public.sections(id) ON DELETE SET NULL;
ALTER TABLE public.suspicious_processes ADD COLUMN IF NOT EXISTS section_id BIGINT NULL REFERENCES public.sections(id) ON DELETE SET NULL;

-- Trigger sync_section_id (forward-compatible con clientes viejos)
CREATE OR REPLACE FUNCTION public.sync_section_id() RETURNS TRIGGER AS $$
BEGIN
  IF NEW.section IS NULL THEN NEW.section_id := NULL;
  ELSIF (TG_OP = 'INSERT' AND NEW.section_id IS NULL)
     OR (TG_OP = 'UPDATE' AND NEW.section IS DISTINCT FROM OLD.section) THEN
    SELECT s.id INTO NEW.section_id FROM public.sections s WHERE s.code = NEW.section LIMIT 1;
  END IF;
  RETURN NEW;
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_sync_section_acceptances ON public.assignment_acceptances;
CREATE TRIGGER trg_sync_section_acceptances BEFORE INSERT OR UPDATE ON public.assignment_acceptances FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();
DROP TRIGGER IF EXISTS trg_sync_section_activity ON public.student_activity;
CREATE TRIGGER trg_sync_section_activity BEFORE INSERT OR UPDATE ON public.student_activity FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();
DROP TRIGGER IF EXISTS trg_sync_section_online ON public.online_clients;
CREATE TRIGGER trg_sync_section_online BEFORE INSERT OR UPDATE ON public.online_clients FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();
DROP TRIGGER IF EXISTS trg_sync_section_alerts ON public.process_alerts;
CREATE TRIGGER trg_sync_section_alerts BEFORE INSERT OR UPDATE ON public.process_alerts FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();
DROP TRIGGER IF EXISTS trg_sync_section_browser ON public.browser_history;
CREATE TRIGGER trg_sync_section_browser BEFORE INSERT OR UPDATE ON public.browser_history FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();
DROP TRIGGER IF EXISTS trg_sync_section_susproc ON public.suspicious_processes;
CREATE TRIGGER trg_sync_section_susproc BEFORE INSERT OR UPDATE ON public.suspicious_processes FOR EACH ROW EXECUTE FUNCTION public.sync_section_id();

-- Backfill: curso default FPY1101 + 3 secciones + 5 evaluaciones
INSERT INTO public.courses (code, name, active) VALUES ('FPY1101', 'Fisica I', true) ON CONFLICT (code) DO NOTHING;
INSERT INTO public.sections (course_id, code, name)
SELECT c.id, s.code, s.name FROM public.courses c
CROSS JOIN (VALUES ('001D','Seccion 001D'), ('002D','Seccion 002D'), ('003D','Seccion 003D')) AS s(code, name)
WHERE c.code = 'FPY1101' ON CONFLICT (course_id, code) DO NOTHING;
INSERT INTO public.evaluations (section_id, title, active)
SELECT s.id, e.title, false FROM public.sections s
CROSS JOIN (VALUES ('Evaluacion-1'), ('Evaluacion-2'), ('Evaluacion-3'), ('Evaluacion-4'), ('Examen')) AS e(title)
WHERE s.course_id = (SELECT id FROM public.courses WHERE code = 'FPY1101')
  AND s.code IN ('001D', '002D', '003D') ON CONFLICT (section_id, title) DO NOTHING;

-- ============================================================
--  5. ASSIGNMENT SUBMISSIONS (entrega formal de repo)
-- ============================================================
ALTER TABLE public.assignments
  ADD COLUMN IF NOT EXISTS allows_manual_submission BOOLEAN NOT NULL DEFAULT FALSE;

CREATE TABLE IF NOT EXISTS public.assignment_submissions (
  id BIGSERIAL PRIMARY KEY,
  assignment_id BIGINT NOT NULL,
  github_username TEXT NOT NULL,
  repo_url TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'submitted',
  submitted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(github_username, assignment_id)
);
CREATE INDEX IF NOT EXISTS idx_submissions_assignment
  ON public.assignment_submissions (assignment_id, submitted_at DESC);
CREATE INDEX IF NOT EXISTS idx_submissions_user
  ON public.assignment_submissions (github_username, submitted_at DESC);

ALTER TABLE public.assignment_submissions ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "anon_insert_submissions" ON public.assignment_submissions;
CREATE POLICY "anon_insert_submissions" ON public.assignment_submissions FOR INSERT WITH CHECK (true);
DROP POLICY IF EXISTS "anon_read_submissions" ON public.assignment_submissions;
CREATE POLICY "anon_read_submissions" ON public.assignment_submissions FOR SELECT TO anon, authenticated USING (true);
DROP POLICY IF EXISTS "anon_update_submissions" ON public.assignment_submissions;
CREATE POLICY "anon_update_submissions" ON public.assignment_submissions FOR UPDATE USING (true) WITH CHECK (true);

DROP FUNCTION IF EXISTS public.record_submission(BIGINT, TEXT, TEXT, TEXT);
CREATE OR REPLACE FUNCTION public.record_submission(
  p_assignment_id BIGINT, p_github_username TEXT, p_repo_url TEXT,
  p_status TEXT DEFAULT 'submitted'
) RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  INSERT INTO public.assignment_submissions
    (assignment_id, github_username, repo_url, status, submitted_at)
  VALUES
    (p_assignment_id, p_github_username, p_repo_url, p_status, NOW())
  ON CONFLICT (github_username, assignment_id) DO UPDATE
  SET repo_url = EXCLUDED.repo_url, status = EXCLUDED.status, submitted_at = NOW();
END; $$;
GRANT EXECUTE ON FUNCTION public.record_submission(BIGINT,TEXT,TEXT,TEXT) TO anon, authenticated;

-- ============================================================
--  6. REALTIME
-- ============================================================
DO $$
DECLARE t text;
BEGIN
  FOREACH t IN ARRAY array['control','online_clients','browser_history','cheat_events',
    'process_alerts','suspicious_processes','courses','sections','evaluations',
    'assignment_acceptances','assignment_submissions'] LOOP
    IF NOT EXISTS (
      SELECT 1 FROM pg_publication_tables
      WHERE pubname = 'supabase_realtime' AND schemaname = 'public' AND tablename = t
    ) THEN
      EXECUTE format('ALTER PUBLICATION supabase_realtime ADD TABLE public.%I', t);
      RAISE NOTICE 'Realtime: public.%', t;
    END IF;
  END LOOP;
END $$;

-- ============================================================
--  7. VERIFICACION
-- ============================================================
SELECT 'courses' AS tabla, COUNT(*) AS filas FROM public.courses
UNION ALL SELECT 'sections', COUNT(*) FROM public.sections
UNION ALL SELECT 'evaluations', COUNT(*) FROM public.evaluations
UNION ALL SELECT 'assignments', COUNT(*) FROM public.assignments
UNION ALL SELECT 'assignment_acceptances', COUNT(*) FROM public.assignment_acceptances
UNION ALL SELECT 'assignment_submissions', COUNT(*) FROM public.assignment_submissions
UNION ALL SELECT 'suspicious_processes', COUNT(*) FROM public.suspicious_processes
UNION ALL SELECT 'online_clients', COUNT(*) FROM public.online_clients;
