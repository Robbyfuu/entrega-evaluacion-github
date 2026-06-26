-- ============================================================
--  Migracion: acotar la lectura anon del bucket exam-pdfs
--  (cierra HIGH sec-backend-04: fuga del enunciado completo).
--
--  ANTES: la policy exam_pdfs_select dejaba a CUALQUIER anon listar y
--  descargar TODOS los PDF de TODAS las secciones (filtro solo por
--  bucket_id), con nombre predecible (eval-<id>.pdf), incluso ANTES de
--  activarse la evaluacion -> fuga del contenido del examen.
--
--  AHORA: anon solo puede descargar el objeto si su nombre coincide con
--  el exam_pdf_path de una evaluacion ACTIVA. El profe (authenticated)
--  sigue viendo todo (para previsualizar/gestionar). No requiere cambios
--  en el cliente: el alumno abre el enunciado cuando la evaluacion esta
--  activa, que es justo cuando esta policy lo permite.
--
--  Idempotente. Correr en Supabase SQL Editor.
-- ============================================================

-- Se reemplaza la policy unica permisiva por DOS policies role-scoped
-- (varias policies permisivas para el mismo comando se combinan con OR).

-- Quitar la policy vieja (anon+authenticated, solo bucket_id).
DROP POLICY IF EXISTS "exam_pdfs_select" ON storage.objects;

-- Profe (authenticated): lee todo el bucket (subir / reemplazar / revisar).
DROP POLICY IF EXISTS "exam_pdfs_select_teacher" ON storage.objects;
CREATE POLICY "exam_pdfs_select_teacher"
  ON storage.objects
  FOR SELECT
  TO authenticated
  USING (bucket_id = 'exam-pdfs');

-- Alumno (anon): solo el PDF de una evaluacion ACTIVA con exam_pdf_path.
DROP POLICY IF EXISTS "exam_pdfs_select_student" ON storage.objects;
CREATE POLICY "exam_pdfs_select_student"
  ON storage.objects
  FOR SELECT
  TO anon
  USING (
    bucket_id = 'exam-pdfs'
    AND name IN (
      SELECT exam_pdf_path
      FROM public.evaluations
      WHERE active = true
        AND exam_pdf_path IS NOT NULL
    )
  );

-- ============================================================
--  RESIDUAL: mientras la evaluacion esta activa, cualquier portador de la
--  anon key puede descargar ESE PDF (es el enunciado en curso, destinado a
--  los alumnos que rinden). Lo que se cierra es la fuga PRE-examen y la de
--  OTRAS secciones/evaluaciones no activas. Acotar por alumno/seccion
--  exige binding de identidad real (JWT por dispositivo), igual que el
--  resto de A01 (ver migration-rls-identity-hardening.sql).
-- ============================================================

-- ============================================================
--  VERIFICACION
-- ============================================================
-- Esperado: la vieja 'exam_pdfs_select' ausente (0); las dos nuevas presentes (2).
SELECT 'exam_pdfs_select (vieja, esperado 0)' AS check, COUNT(*) AS n
  FROM pg_policies
  WHERE schemaname = 'storage' AND tablename = 'objects'
    AND policyname = 'exam_pdfs_select'
UNION ALL
SELECT 'exam_pdfs_select_teacher/student (esperado 2)', COUNT(*)
  FROM pg_policies
  WHERE schemaname = 'storage' AND tablename = 'objects'
    AND policyname IN ('exam_pdfs_select_teacher', 'exam_pdfs_select_student');
