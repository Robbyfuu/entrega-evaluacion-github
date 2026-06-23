-- ============================================================
--  Migracion: allowed_urls (allowlist del navegador embebido,
--  editable por seccion desde el panel).
--  Idempotente. Correr en Supabase SQL Editor.
--
--  Espejo de suspicious_processes (blocklist), pero AL REVES: aqui se
--  define lo PERMITIDO. Modelo: section IS NULL = regla GLOBAL (heredada
--  por todas las secciones); section = 'X' = extra de la seccion X. La
--  lista efectiva de un alumno de la seccion X es (section IS NULL)
--  UNION (section = 'X').
--
--  kind discrimina los DOS tipos de regla que ya existian hardcodeados
--  en Config.cs:
--    'domain'    -> match por SUFIJO de host (IsDomainAllowed): la fila
--                   y sus subdominios. Ej: 'github.com' permite
--                   classroom.github.com. NO uses TLDs ni dominios
--                   genericos amplios (serian un escape).
--    'exact_url' -> match por PREFIJO de scheme://host/path (IsUrlAllowed):
--                   habilita una ruta puntual sin abrir el host entero.
--                   Ej: 'https://www.google.com/a/duocuc.cl/acs'.
--
--  SEGURIDAD: el cliente C# usa esta tabla como fuente de verdad pero
--  CAE a Config.AllowedBrowseDomains/AllowedExactUrls (hardcode) si el
--  fetch falla o vuelve vacio. El fallback es la lista MAS restrictiva
--  conocida: un fetch fallido NUNCA amplia lo permitido.
-- ============================================================

-- ============================================================
--  1. TABLA allowed_urls
-- ============================================================
CREATE TABLE IF NOT EXISTS public.allowed_urls (
  id BIGSERIAL PRIMARY KEY,
  pattern TEXT NOT NULL,
  kind TEXT NOT NULL DEFAULT 'domain' CHECK (kind IN ('domain', 'exact_url')),
  section TEXT,
  section_id BIGINT REFERENCES public.sections(id) ON DELETE SET NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Unicidad por (pattern, kind, section) tratando NULL como '' para que
-- el seed y los inserts futuros sean idempotentes (ON CONFLICT).
CREATE UNIQUE INDEX IF NOT EXISTS ux_allowed_urls_pattern_kind_section
  ON public.allowed_urls (pattern, kind, COALESCE(section, ''));

-- Indice de apoyo para la query del cliente (filtra por seccion).
CREATE INDEX IF NOT EXISTS idx_allowed_urls_section
  ON public.allowed_urls (section);

-- ============================================================
--  2. ROW LEVEL SECURITY
--  anon + authenticated leen (el cliente C# lee con anon key);
--  solo authenticated escribe (CRUD del profe en el panel).
-- ============================================================
ALTER TABLE public.allowed_urls ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "anon_read_allowed_urls" ON public.allowed_urls;
CREATE POLICY "anon_read_allowed_urls" ON public.allowed_urls
  FOR SELECT TO anon, authenticated USING (true);

DROP POLICY IF EXISTS "auth_all_allowed_urls" ON public.allowed_urls;
CREATE POLICY "auth_all_allowed_urls" ON public.allowed_urls
  FOR ALL TO authenticated USING (true) WITH CHECK (true);

-- ============================================================
--  3. SEED de las reglas globales (section = NULL)
--  Copiado de Config.AllowedBrowseDomains (kind='domain') y
--  Config.AllowedExactUrls (kind='exact_url'). Mantener sincronizado
--  con el fallback de Config.cs. Idempotente (ON CONFLICT).
-- ============================================================
INSERT INTO public.allowed_urls (pattern, kind, section)
SELECT pat, 'domain', NULL
FROM unnest(ARRAY[
  'github.com',
  'githubusercontent.com',
  'login.microsoftonline.com',
  'login.live.com',
  'login.microsoft.com',
  'msftauth.net',
  'aadcdn.msftauth.net',
  'accounts.google.com',
  'accounts.google.cl',
  'mail.google.com',
  'googleusercontent.com'
]) AS pat
ON CONFLICT (pattern, kind, COALESCE(section, '')) DO NOTHING;

INSERT INTO public.allowed_urls (pattern, kind, section)
SELECT pat, 'exact_url', NULL
FROM unnest(ARRAY[
  'https://www.google.com/a/duocuc.cl/acs'
]) AS pat
ON CONFLICT (pattern, kind, COALESCE(section, '')) DO NOTHING;

-- ============================================================
--  4. Realtime: agregar allowed_urls a la publicacion.
--  Guardado contra duplicate_object para que re-correr no falle.
-- ============================================================
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_publication_tables
    WHERE pubname = 'supabase_realtime'
      AND schemaname = 'public'
      AND tablename = 'allowed_urls'
  ) THEN
    EXECUTE 'ALTER PUBLICATION supabase_realtime ADD TABLE public.allowed_urls';
    RAISE NOTICE 'Realtime habilitado para public.allowed_urls';
  ELSE
    RAISE NOTICE 'public.allowed_urls ya estaba en supabase_realtime (sin cambios)';
  END IF;
EXCEPTION
  WHEN duplicate_object THEN
    RAISE NOTICE 'public.allowed_urls ya estaba en la publicacion (duplicate_object)';
END $$;

-- ============================================================
--  5. VERIFICACION
-- ============================================================
SELECT 'allowed_urls' AS tabla, COUNT(*) AS filas FROM public.allowed_urls
UNION ALL
SELECT 'globales dominio (section NULL)', COUNT(*) FROM public.allowed_urls WHERE section IS NULL AND kind = 'domain'
UNION ALL
SELECT 'globales url exacta (section NULL)', COUNT(*) FROM public.allowed_urls WHERE section IS NULL AND kind = 'exact_url';
