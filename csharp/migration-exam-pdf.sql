-- ============================================================
--  Migracion: PDF de enunciado por evaluacion
--  - evaluations.exam_pdf_path : path del objeto en Storage que apunta al
--    PDF del enunciado (ej 'eval-49.pdf'). NULL => la evaluacion no tiene PDF.
--  - Bucket privado 'exam-pdfs' en Storage. El panel (authenticated) sube,
--    reemplaza y borra; el cliente C# (anon) lo descarga via RLS SELECT.
--  Idempotente. Correr en Supabase SQL Editor.
-- ============================================================

-- 1. Columna nueva ------------------------------------------------------
ALTER TABLE public.evaluations
  ADD COLUMN IF NOT EXISTS exam_pdf_path TEXT;

-- 2. Bucket privado de Storage -----------------------------------------
-- public=false => no hay URL publica directa: toda descarga pasa por RLS.
INSERT INTO storage.buckets (id, name, public)
VALUES ('exam-pdfs', 'exam-pdfs', false)
ON CONFLICT (id) DO NOTHING;

-- 3. Politicas RLS sobre storage.objects (bucket 'exam-pdfs') -----------
-- SELECT (descarga): anon + authenticated. El cliente del alumno usa la
-- anon key, asi que necesita poder leer el objeto del enunciado.
DROP POLICY IF EXISTS "exam_pdfs_select" ON storage.objects;
CREATE POLICY "exam_pdfs_select"
  ON storage.objects
  FOR SELECT
  TO anon, authenticated
  USING (bucket_id = 'exam-pdfs');

-- INSERT/UPDATE/DELETE (gestion): solo el panel (authenticated).
DROP POLICY IF EXISTS "exam_pdfs_insert" ON storage.objects;
CREATE POLICY "exam_pdfs_insert"
  ON storage.objects
  FOR INSERT
  TO authenticated
  WITH CHECK (bucket_id = 'exam-pdfs');

DROP POLICY IF EXISTS "exam_pdfs_update" ON storage.objects;
CREATE POLICY "exam_pdfs_update"
  ON storage.objects
  FOR UPDATE
  TO authenticated
  USING (bucket_id = 'exam-pdfs')
  WITH CHECK (bucket_id = 'exam-pdfs');

DROP POLICY IF EXISTS "exam_pdfs_delete" ON storage.objects;
CREATE POLICY "exam_pdfs_delete"
  ON storage.objects
  FOR DELETE
  TO authenticated
  USING (bucket_id = 'exam-pdfs');

-- 4. Verificacion -------------------------------------------------------
SELECT 'evaluations.exam_pdf_path' AS check, COUNT(*) AS filas_con_pdf
FROM public.evaluations WHERE exam_pdf_path IS NOT NULL
UNION ALL
SELECT 'storage.buckets exam-pdfs', COUNT(*)
FROM storage.buckets WHERE id = 'exam-pdfs';
