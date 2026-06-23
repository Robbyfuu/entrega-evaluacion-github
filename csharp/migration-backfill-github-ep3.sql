-- ============================================================
--  Backfill: enrollments.github_username desde datos de EP3
--
--  PROPOSITO
--  Completar enrollments.github_username para los alumnos que entregaron
--  la EP3 via GitHub Classroom, usando como fuente PRIMARIA el login de
--  GitHub AUTENTICADO que el cliente C# registro durante la evaluacion
--  (student_activity / assignment_acceptances tienen repo_url +
--  github_username), y como fallback HEURISTICO el login parseado del
--  sufijo del nombre del repo de Classroom.
--
--  GARANTIAS
--  - Idempotente: solo toca filas con github_username IS NULL (re-correr
--    no repisa logins ya asignados por el docente ni por una corrida
--    previa).
--  - Aditivo: NO hace DROP/ALTER/CREATE de schema. Solo UPDATE de datos.
--  - Nunca aborta: ningun statement falla si las tablas estan vacias o
--    no hay matches; los reportes finales devuelven 0 filas sin error.
--  - Respeta el UNIQUE parcial ux_enroll_section_github
--    (section_id, lower(github_username)) WHERE github_username IS NOT NULL:
--    si asignar el login chocaria con otra inscripcion de la MISMA seccion
--    que ya lo tiene, la fila se OMITE en vez de violar la restriccion.
--
--  PRECEDENCIA DE FUENTE (por alumno)
--    1. 'db-exact'        -> login autenticado, JOIN inventario.repo_url
--                            contra student_activity/assignment_acceptances.
--                            EXACTO, sin adivinar.
--    2. 'suffix-heuristic'-> login parseado del sufijo del repo (cuando
--                            no hubo match en la DB y hay sufijo).
--    3. 'none'            -> ni match exacto ni sufijo (no se actualiza).
--
--  ORDEN DE EJECUCION: correr DESPUES de migration-enrollments.sql.
--  Tablas leidas: enrollments, student_activity, assignment_acceptances.
-- ============================================================

-- ============================================================
--  Backfill envuelto en una CTE de calculo + UPDATE ... FROM.
--  bb         = inventario EP3 (ground truth de Blackboard).
--  auth       = login autenticado por repo (de la DB), sin ambiguos.
--  resolution = COALESCE(auth.github, bb.suffix_candidate) + source.
-- ============================================================

WITH
-- ------------------------------------------------------------
-- bb: las 66 entregas EP3 embebidas (ground truth de Blackboard).
-- blackboard_id es la MISMA clave que enrollments.blackboard_student_id.
-- suffix_candidate = login parseado del sufijo del nombre del repo de
-- Classroom (NULL cuando no se pudo parsear).
-- ------------------------------------------------------------
bb (blackboard_id, section_code, repo_url, suffix_candidate) AS (
  VALUES
    ('_931305_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-Varelas-2908', 'Varelas-2908'),
    ('_719100_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-IX-bot571', 'IX-bot571'),
    ('_889514_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-concornejo-arch', 'concornejo-arch'),
    ('_641092_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-SebastianTroncoso360', 'SebastianTroncoso360'),
    ('_640559_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-esdelgado-duoc', 'esdelgado-duoc'),
    ('_893157_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-arlandero', 'arlandero'),
    ('_893167_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-fransoza-web', 'fransoza-web'),
    ('_887872_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-RauLetelier', 'RauLetelier'),
    ('_890355_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-SAbbott1', 'SAbbott1'),
    ('_887968_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-marcojaramillo1203', 'marcojaramillo1203'),
    ('_900274_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-alspiniello', 'alspiniello'),
    ('_890350_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-awooga93', 'awooga93'),
    ('_892362_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-josetomasfernandez', 'josetomasfernandez'),
    ('_890375_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-Lucasszzzzz', 'Lucasszzzzz'),
    ('_200239_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-juacgonzalezv-boop', 'juacgonzalezv-boop'),
    ('_889082_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-asrios-gif', 'asrios-gif'),
    ('_891851_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-MateoLopezDuoc', 'MateoLopezDuoc'),
    ('_885179_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-anfonfriaduocuc', 'anfonfriaduocuc'),
    ('_889090_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-joriveaux-duoc', 'joriveaux-duoc'),
    ('_930677_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-jucelisv-ctrl', 'jucelisv-ctrl'),
    ('_800610_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-alfonsoalvarez-duoc', 'alfonsoalvarez-duoc'),
    ('_913689_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-Nico-Val51', 'Nico-Val51'),
    ('_916544_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-benalvram', 'benalvram'),
    ('_918956_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-LordZar616', 'LordZar616'),
    ('_918721_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-ricardo-svg-code', 'ricardo-svg-code'),
    ('_903436_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-cescid-commits', 'cescid-commits'),
    ('_905881_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-benlopeza', 'benlopeza'),
    ('_916551_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-cristocortesc-hub', 'cristocortesc-hub'),
    ('_912025_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-Amalia-Aguilar', 'Amalia-Aguilar'),
    ('_904237_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-nicerrazuriz-sudo', 'nicerrazuriz-sudo'),
    ('_906185_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-BenitoFTS', 'BenitoFTS'),
    ('_909291_1', '002D', 'https://github.com/dopeAFjoak0/joaquin-antonio-hoeneisen-gonzalez-evaluacion-3', NULL),
    ('_920917_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-joaherrerafduoc', 'joaherrerafduoc'),
    ('_909672_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-matcids-lang', 'matcids-lang'),
    ('_919675_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-MSeguel-bot', NULL),
    ('_919532_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-benjvalenduoc', 'benjvalenduoc'),
    ('_911759_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-m-retamala', 'm-retamala'),
    ('_920695_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-frmmtb', 'frmmtb'),
    ('_783330_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-moixkl0', 'moixkl0'),
    ('_928901_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-PoetaMaldito13-duoc', 'PoetaMaldito13-duoc'),
    ('_928862_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-samuelmontana', 'samuelmontana'),
    ('_925536_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-juando78', 'juando78'),
    ('_924375_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-met0r1i', 'met0r1i'),
    ('_924543_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-enciye4', 'enciye4'),
    ('_927835_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-K4RNAZ4', 'K4RNAZ4'),
    ('_921333_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Paula-Olave', 'Paula-Olave'),
    ('_923019_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Isidora-Amestica', 'Isidora-Amestica'),
    ('_922072_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Romaaaaaaaaaaaaaaaaa', 'Romaaaaaaaaaaaaaaaaa'),
    ('_921496_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Matias-Briones', 'Matias-Briones'),
    ('_172295_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Walomech', 'Walomech'),
    ('_925853_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-CrPrArGa', 'CrPrArGa'),
    ('_160192_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-frborgono', 'frborgono'),
    ('_923648_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-JaviRGT', 'JaviRGT'),
    ('_925762_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-chagod01', 'chagod01'),
    ('_924730_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-gableon996', 'gableon996'),
    ('_921465_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-RemiTheCheff', 'RemiTheCheff'),
    ('_924233_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-stgonzaleza-byte', 'stgonzaleza-byte'),
    ('_921813_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-deqo67', 'deqo67'),
    ('_924820_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-awooga93', 'awooga93'),
    ('_923400_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-salo400', 'salo400'),
    ('_923401_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-alvaro-cesar', 'alvaro-cesar'),
    ('_921312_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-gilberrt7', NULL),
    ('_924813_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-yomiyokybun', 'yomiyokybun'),
    ('_923640_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-SheepDuocUC', 'SheepDuocUC'),
    ('_924351_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-gmmf-fus', 'gmmf-fus'),
    ('_924180_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Bastian011', 'Bastian011')
),

-- ------------------------------------------------------------
-- db_pairs: union de (repo_url, github_username) provenientes de las
-- tablas que tienen AMBAS columnas. student_activity es la fuente
-- principal (clone/create_repo/upload graban repo_url + login
-- autenticado). assignment_acceptances aporta los acepta-repo.
-- Filtramos filas sin repo_url o sin login (no aportan join key).
-- ------------------------------------------------------------
db_pairs (repo_url, github) AS (
  SELECT repo_url, github_username
    FROM public.student_activity
   WHERE repo_url IS NOT NULL AND github_username IS NOT NULL
  UNION ALL
  SELECT repo_url, github_username
    FROM public.assignment_acceptances
   WHERE repo_url IS NOT NULL AND github_username IS NOT NULL
),

-- ------------------------------------------------------------
-- db_norm: normaliza la clave de join.
--   url_key  = url en minusculas, sin "/" final y sin sufijo ".git".
--   name_key = ultimo segmento del path (nombre del repo) en minusculas.
-- name_key permite reconciliar diferencias de host/owner/case cuando la
-- url completa no coincide exacto.
-- ------------------------------------------------------------
db_norm (url_key, name_key, github) AS (
  SELECT
    regexp_replace(regexp_replace(lower(repo_url), '\.git$', ''), '/+$', '') AS url_key,
    lower(
      regexp_replace(
        regexp_replace(regexp_replace(lower(repo_url), '\.git$', ''), '/+$', ''),
        '^.*/', ''
      )
    ) AS name_key,
    github
  FROM db_pairs
),

-- ------------------------------------------------------------
-- auth_by_url: login autenticado por url_key, EXCLUYENDO los ambiguos
-- (un mismo repo que aparece con >1 login distinto). Si es ambiguo no
-- adivinamos: lo dejamos fuera para que caiga al fallback heuristico.
-- ------------------------------------------------------------
auth_by_url (url_key, github) AS (
  SELECT url_key, MIN(github) AS github
    FROM (SELECT DISTINCT url_key, github FROM db_norm) u
   GROUP BY url_key
  HAVING COUNT(*) = 1
),

-- ------------------------------------------------------------
-- auth_by_name: idem por name_key (fallback de matcheo cuando la url
-- completa difiere). Tambien excluye ambiguos.
-- ------------------------------------------------------------
auth_by_name (name_key, github) AS (
  SELECT name_key, MIN(github) AS github
    FROM (SELECT DISTINCT name_key, github FROM db_norm) n
   GROUP BY name_key
  HAVING COUNT(*) = 1
),

-- ------------------------------------------------------------
-- bb_norm: normaliza la url del inventario con la MISMA receta, mas el
-- name_key del repo, para poder cruzar contra auth_by_url / auth_by_name.
-- ------------------------------------------------------------
bb_norm AS (
  SELECT
    bb.blackboard_id,
    bb.section_code,
    bb.suffix_candidate,
    regexp_replace(regexp_replace(lower(bb.repo_url), '\.git$', ''), '/+$', '') AS url_key,
    lower(
      regexp_replace(
        regexp_replace(regexp_replace(lower(bb.repo_url), '\.git$', ''), '/+$', ''),
        '^.*/', ''
      )
    ) AS name_key
  FROM bb
),

-- ------------------------------------------------------------
-- resolution: por cada fila del inventario, resuelve el login y la
-- fuente. Prioridad EXACTO antes que heuristico:
--   db_github  = match por url_key, si no por name_key.
--   github     = COALESCE(db_github, suffix_candidate).
--   source     = 'db-exact'        cuando db_github no es NULL;
--                'suffix-heuristic' cuando solo hay suffix_candidate;
--                'none'            cuando no hay ninguno.
-- ------------------------------------------------------------
resolution AS (
  SELECT
    n.blackboard_id,
    n.section_code,
    n.suffix_candidate,
    COALESCE(au.github, an.github) AS db_github,
    COALESCE(COALESCE(au.github, an.github), n.suffix_candidate) AS github,
    CASE
      WHEN COALESCE(au.github, an.github) IS NOT NULL THEN 'db-exact'
      WHEN n.suffix_candidate IS NOT NULL              THEN 'suffix-heuristic'
      ELSE 'none'
    END AS source
  FROM bb_norm n
  LEFT JOIN auth_by_url  au ON au.url_key  = n.url_key
  LEFT JOIN auth_by_name an ON an.name_key = n.name_key
)

-- ============================================================
--  UPDATE: aplica el login resuelto SOLO a inscripciones con github
--  NULL (idempotente). El guard NOT EXISTS evita violar el UNIQUE
--  parcial por seccion: si otra inscripcion de la misma seccion ya
--  reclama ese login (case-insensitive), la fila se OMITE en silencio.
-- ============================================================
UPDATE public.enrollments e
   SET github_username = r.github,
       updated_at      = NOW()
  FROM resolution r
 WHERE e.blackboard_student_id = r.blackboard_id
   AND e.github_username IS NULL
   AND r.github IS NOT NULL
   AND NOT EXISTS (
     SELECT 1
       FROM public.enrollments x
      WHERE x.section_id = e.section_id
        AND lower(x.github_username) = lower(r.github)
        AND x.id <> e.id
   );

-- ============================================================
--  REPORTES (read-only; ninguno aborta con datos vacios)
-- ============================================================

-- ------------------------------------------------------------
-- R1. Detalle por alumno actualizado: que login quedo y de que fuente
-- ('db-exact' = autenticado, 'suffix-heuristic' = parseado). Reconstruye
-- la misma resolucion y la cruza contra enrollments para mostrar SOLO
-- los que efectivamente quedaron con ese login (los que el UPDATE toco
-- o ya tenian, distinguidos por la comparacion case-insensitive).
-- ------------------------------------------------------------
WITH
bb (blackboard_id, section_code, repo_url, suffix_candidate) AS (
  VALUES
    ('_931305_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-Varelas-2908', 'Varelas-2908'),
    ('_719100_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-IX-bot571', 'IX-bot571'),
    ('_889514_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-concornejo-arch', 'concornejo-arch'),
    ('_641092_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-SebastianTroncoso360', 'SebastianTroncoso360'),
    ('_640559_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-esdelgado-duoc', 'esdelgado-duoc'),
    ('_893157_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-arlandero', 'arlandero'),
    ('_893167_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-fransoza-web', 'fransoza-web'),
    ('_887872_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-RauLetelier', 'RauLetelier'),
    ('_890355_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-SAbbott1', 'SAbbott1'),
    ('_887968_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-marcojaramillo1203', 'marcojaramillo1203'),
    ('_900274_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-alspiniello', 'alspiniello'),
    ('_890350_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-awooga93', 'awooga93'),
    ('_892362_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-josetomasfernandez', 'josetomasfernandez'),
    ('_890375_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-Lucasszzzzz', 'Lucasszzzzz'),
    ('_200239_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-juacgonzalezv-boop', 'juacgonzalezv-boop'),
    ('_889082_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-asrios-gif', 'asrios-gif'),
    ('_891851_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-MateoLopezDuoc', 'MateoLopezDuoc'),
    ('_885179_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-anfonfriaduocuc', 'anfonfriaduocuc'),
    ('_889090_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-joriveaux-duoc', 'joriveaux-duoc'),
    ('_930677_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-jucelisv-ctrl', 'jucelisv-ctrl'),
    ('_800610_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-alfonsoalvarez-duoc', 'alfonsoalvarez-duoc'),
    ('_913689_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-Nico-Val51', 'Nico-Val51'),
    ('_916544_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-benalvram', 'benalvram'),
    ('_918956_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-LordZar616', 'LordZar616'),
    ('_918721_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-ricardo-svg-code', 'ricardo-svg-code'),
    ('_903436_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-cescid-commits', 'cescid-commits'),
    ('_905881_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-benlopeza', 'benlopeza'),
    ('_916551_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-cristocortesc-hub', 'cristocortesc-hub'),
    ('_912025_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-Amalia-Aguilar', 'Amalia-Aguilar'),
    ('_904237_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-nicerrazuriz-sudo', 'nicerrazuriz-sudo'),
    ('_906185_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-BenitoFTS', 'BenitoFTS'),
    ('_909291_1', '002D', 'https://github.com/dopeAFjoak0/joaquin-antonio-hoeneisen-gonzalez-evaluacion-3', NULL),
    ('_920917_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-joaherrerafduoc', 'joaherrerafduoc'),
    ('_909672_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-matcids-lang', 'matcids-lang'),
    ('_919675_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-MSeguel-bot', NULL),
    ('_919532_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-benjvalenduoc', 'benjvalenduoc'),
    ('_911759_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-m-retamala', 'm-retamala'),
    ('_920695_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-frmmtb', 'frmmtb'),
    ('_783330_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-moixkl0', 'moixkl0'),
    ('_928901_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-PoetaMaldito13-duoc', 'PoetaMaldito13-duoc'),
    ('_928862_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-samuelmontana', 'samuelmontana'),
    ('_925536_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-juando78', 'juando78'),
    ('_924375_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-met0r1i', 'met0r1i'),
    ('_924543_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-enciye4', 'enciye4'),
    ('_927835_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-K4RNAZ4', 'K4RNAZ4'),
    ('_921333_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Paula-Olave', 'Paula-Olave'),
    ('_923019_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Isidora-Amestica', 'Isidora-Amestica'),
    ('_922072_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Romaaaaaaaaaaaaaaaaa', 'Romaaaaaaaaaaaaaaaaa'),
    ('_921496_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Matias-Briones', 'Matias-Briones'),
    ('_172295_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Walomech', 'Walomech'),
    ('_925853_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-CrPrArGa', 'CrPrArGa'),
    ('_160192_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-frborgono', 'frborgono'),
    ('_923648_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-JaviRGT', 'JaviRGT'),
    ('_925762_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-chagod01', 'chagod01'),
    ('_924730_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-gableon996', 'gableon996'),
    ('_921465_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-RemiTheCheff', 'RemiTheCheff'),
    ('_924233_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-stgonzaleza-byte', 'stgonzaleza-byte'),
    ('_921813_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-deqo67', 'deqo67'),
    ('_924820_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-awooga93', 'awooga93'),
    ('_923400_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-salo400', 'salo400'),
    ('_923401_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-alvaro-cesar', 'alvaro-cesar'),
    ('_921312_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-gilberrt7', NULL),
    ('_924813_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-yomiyokybun', 'yomiyokybun'),
    ('_923640_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-SheepDuocUC', 'SheepDuocUC'),
    ('_924351_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-gmmf-fus', 'gmmf-fus'),
    ('_924180_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Bastian011', 'Bastian011')
),
db_pairs (repo_url, github) AS (
  SELECT repo_url, github_username
    FROM public.student_activity
   WHERE repo_url IS NOT NULL AND github_username IS NOT NULL
  UNION ALL
  SELECT repo_url, github_username
    FROM public.assignment_acceptances
   WHERE repo_url IS NOT NULL AND github_username IS NOT NULL
),
db_norm (url_key, name_key, github) AS (
  SELECT
    regexp_replace(regexp_replace(lower(repo_url), '\.git$', ''), '/+$', '') AS url_key,
    lower(
      regexp_replace(
        regexp_replace(regexp_replace(lower(repo_url), '\.git$', ''), '/+$', ''),
        '^.*/', ''
      )
    ) AS name_key,
    github
  FROM db_pairs
),
auth_by_url (url_key, github) AS (
  SELECT url_key, MIN(github) AS github
    FROM (SELECT DISTINCT url_key, github FROM db_norm) u
   GROUP BY url_key
  HAVING COUNT(*) = 1
),
auth_by_name (name_key, github) AS (
  SELECT name_key, MIN(github) AS github
    FROM (SELECT DISTINCT name_key, github FROM db_norm) n
   GROUP BY name_key
  HAVING COUNT(*) = 1
),
bb_norm AS (
  SELECT
    bb.blackboard_id,
    bb.section_code,
    bb.suffix_candidate,
    regexp_replace(regexp_replace(lower(bb.repo_url), '\.git$', ''), '/+$', '') AS url_key,
    lower(
      regexp_replace(
        regexp_replace(regexp_replace(lower(bb.repo_url), '\.git$', ''), '/+$', ''),
        '^.*/', ''
      )
    ) AS name_key
  FROM bb
),
resolution AS (
  SELECT
    n.blackboard_id,
    n.section_code,
    COALESCE(COALESCE(au.github, an.github), n.suffix_candidate) AS github,
    CASE
      WHEN COALESCE(au.github, an.github) IS NOT NULL THEN 'db-exact'
      WHEN n.suffix_candidate IS NOT NULL              THEN 'suffix-heuristic'
      ELSE 'none'
    END AS source
  FROM bb_norm n
  LEFT JOIN auth_by_url  au ON au.url_key  = n.url_key
  LEFT JOIN auth_by_name an ON an.name_key = n.name_key
)
SELECT
  r.blackboard_id,
  r.section_code,
  r.github            AS github_resuelto,
  r.source            AS fuente,
  e.github_username   AS github_en_enrollments
FROM resolution r
JOIN public.enrollments e
  ON e.blackboard_student_id = r.blackboard_id
WHERE r.github IS NOT NULL
  AND e.github_username IS NOT NULL
  AND lower(e.github_username) = lower(r.github)
ORDER BY r.section_code, r.source, r.blackboard_id;

-- ------------------------------------------------------------
-- R2. Resumen de conteo por fuente sobre el inventario completo (66):
-- cuantos resuelven via 'db-exact', 'suffix-heuristic' o 'none'. Es la
-- elegibilidad TEORICA segun la data; el UPDATE real puede ser menor por
-- el guard de colision o por filas ya completas.
-- ------------------------------------------------------------
WITH
bb (blackboard_id, section_code, repo_url, suffix_candidate) AS (
  VALUES
    ('_931305_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-Varelas-2908', 'Varelas-2908'),
    ('_719100_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-IX-bot571', 'IX-bot571'),
    ('_889514_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-concornejo-arch', 'concornejo-arch'),
    ('_641092_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-SebastianTroncoso360', 'SebastianTroncoso360'),
    ('_640559_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-esdelgado-duoc', 'esdelgado-duoc'),
    ('_893157_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-arlandero', 'arlandero'),
    ('_893167_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-fransoza-web', 'fransoza-web'),
    ('_887872_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-RauLetelier', 'RauLetelier'),
    ('_890355_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-SAbbott1', 'SAbbott1'),
    ('_887968_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-marcojaramillo1203', 'marcojaramillo1203'),
    ('_900274_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-alspiniello', 'alspiniello'),
    ('_890350_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-awooga93', 'awooga93'),
    ('_892362_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-josetomasfernandez', 'josetomasfernandez'),
    ('_890375_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-Lucasszzzzz', 'Lucasszzzzz'),
    ('_200239_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-juacgonzalezv-boop', 'juacgonzalezv-boop'),
    ('_889082_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-asrios-gif', 'asrios-gif'),
    ('_891851_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-MateoLopezDuoc', 'MateoLopezDuoc'),
    ('_885179_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-anfonfriaduocuc', 'anfonfriaduocuc'),
    ('_889090_1', '001D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-joriveaux-duoc', 'joriveaux-duoc'),
    ('_930677_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-jucelisv-ctrl', 'jucelisv-ctrl'),
    ('_800610_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-alfonsoalvarez-duoc', 'alfonsoalvarez-duoc'),
    ('_913689_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-Nico-Val51', 'Nico-Val51'),
    ('_916544_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-benalvram', 'benalvram'),
    ('_918956_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-LordZar616', 'LordZar616'),
    ('_918721_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-ricardo-svg-code', 'ricardo-svg-code'),
    ('_903436_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-cescid-commits', 'cescid-commits'),
    ('_905881_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-benlopeza', 'benlopeza'),
    ('_916551_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-cristocortesc-hub', 'cristocortesc-hub'),
    ('_912025_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-Amalia-Aguilar', 'Amalia-Aguilar'),
    ('_904237_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-nicerrazuriz-sudo', 'nicerrazuriz-sudo'),
    ('_906185_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-BenitoFTS', 'BenitoFTS'),
    ('_909291_1', '002D', 'https://github.com/dopeAFjoak0/joaquin-antonio-hoeneisen-gonzalez-evaluacion-3', NULL),
    ('_920917_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-joaherrerafduoc', 'joaherrerafduoc'),
    ('_909672_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-matcids-lang', 'matcids-lang'),
    ('_919675_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-MSeguel-bot', NULL),
    ('_919532_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-benjvalenduoc', 'benjvalenduoc'),
    ('_911759_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-m-retamala', 'm-retamala'),
    ('_920695_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-frmmtb', 'frmmtb'),
    ('_783330_1', '002D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-002d-moixkl0', 'moixkl0'),
    ('_928901_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-PoetaMaldito13-duoc', 'PoetaMaldito13-duoc'),
    ('_928862_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-samuelmontana', 'samuelmontana'),
    ('_925536_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-juando78', 'juando78'),
    ('_924375_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-met0r1i', 'met0r1i'),
    ('_924543_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-enciye4', 'enciye4'),
    ('_927835_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-K4RNAZ4', 'K4RNAZ4'),
    ('_921333_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Paula-Olave', 'Paula-Olave'),
    ('_923019_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Isidora-Amestica', 'Isidora-Amestica'),
    ('_922072_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Romaaaaaaaaaaaaaaaaa', 'Romaaaaaaaaaaaaaaaaa'),
    ('_921496_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Matias-Briones', 'Matias-Briones'),
    ('_172295_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Walomech', 'Walomech'),
    ('_925853_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-CrPrArGa', 'CrPrArGa'),
    ('_160192_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-frborgono', 'frborgono'),
    ('_923648_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-JaviRGT', 'JaviRGT'),
    ('_925762_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-chagod01', 'chagod01'),
    ('_924730_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-gableon996', 'gableon996'),
    ('_921465_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-RemiTheCheff', 'RemiTheCheff'),
    ('_924233_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-stgonzaleza-byte', 'stgonzaleza-byte'),
    ('_921813_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-deqo67', 'deqo67'),
    ('_924820_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-awooga93', 'awooga93'),
    ('_923400_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-salo400', 'salo400'),
    ('_923401_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-alvaro-cesar', 'alvaro-cesar'),
    ('_921312_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-gilberrt7', NULL),
    ('_924813_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-yomiyokybun', 'yomiyokybun'),
    ('_923640_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-SheepDuocUC', 'SheepDuocUC'),
    ('_924351_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-gmmf-fus', 'gmmf-fus'),
    ('_924180_1', '003D', 'https://github.com/Fundamentos-de-la-Programacion/evaluacion-3-003d-Bastian011', 'Bastian011')
),
db_pairs (repo_url, github) AS (
  SELECT repo_url, github_username
    FROM public.student_activity
   WHERE repo_url IS NOT NULL AND github_username IS NOT NULL
  UNION ALL
  SELECT repo_url, github_username
    FROM public.assignment_acceptances
   WHERE repo_url IS NOT NULL AND github_username IS NOT NULL
),
db_norm (url_key, name_key, github) AS (
  SELECT
    regexp_replace(regexp_replace(lower(repo_url), '\.git$', ''), '/+$', '') AS url_key,
    lower(
      regexp_replace(
        regexp_replace(regexp_replace(lower(repo_url), '\.git$', ''), '/+$', ''),
        '^.*/', ''
      )
    ) AS name_key,
    github
  FROM db_pairs
),
auth_by_url (url_key, github) AS (
  SELECT url_key, MIN(github) AS github
    FROM (SELECT DISTINCT url_key, github FROM db_norm) u
   GROUP BY url_key
  HAVING COUNT(*) = 1
),
auth_by_name (name_key, github) AS (
  SELECT name_key, MIN(github) AS github
    FROM (SELECT DISTINCT name_key, github FROM db_norm) n
   GROUP BY name_key
  HAVING COUNT(*) = 1
),
bb_norm AS (
  SELECT
    bb.blackboard_id,
    bb.section_code,
    bb.suffix_candidate,
    regexp_replace(regexp_replace(lower(bb.repo_url), '\.git$', ''), '/+$', '') AS url_key,
    lower(
      regexp_replace(
        regexp_replace(regexp_replace(lower(bb.repo_url), '\.git$', ''), '/+$', ''),
        '^.*/', ''
      )
    ) AS name_key
  FROM bb
),
resolution AS (
  SELECT
    CASE
      WHEN COALESCE(au.github, an.github) IS NOT NULL THEN 'db-exact'
      WHEN n.suffix_candidate IS NOT NULL              THEN 'suffix-heuristic'
      ELSE 'none'
    END AS source
  FROM bb_norm n
  LEFT JOIN auth_by_url  au ON au.url_key  = n.url_key
  LEFT JOIN auth_by_name an ON an.name_key = n.name_key
)
SELECT source AS fuente, COUNT(*) AS cantidad
FROM resolution
GROUP BY source
ORDER BY source;

-- ------------------------------------------------------------
-- R3. Resto manual: inscripciones que SIGUEN con github NULL despues del
-- backfill, por seccion. Es la lista que el docente debe completar a mano
-- en el panel.
-- ------------------------------------------------------------
SELECT
  e.section_id,
  e.blackboard_student_id,
  e.full_name
FROM public.enrollments e
WHERE e.github_username IS NULL
ORDER BY e.section_id, e.full_name;
