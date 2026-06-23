-- ============================================================
--  Migracion: Backfill best-effort de atribucion historica
--  (section_id + evaluation_id en filas legacy)
--
--  Idempotente y RE-CORRIBLE. Correr en Supabase SQL Editor.
--
--  Capa de DATOS (solo UPDATE) ENCIMA de:
--    - migration-multi-evaluation.sql (provee sections,
--      evaluations, section_id nullable + trigger sync_section_id)
--    - migration-evaluation-control.sql / PR1 (provee evaluation_id
--      nullable en process_alerts, browser_history, student_activity,
--      online_clients, cheat_events)
--
--  ORDEN: correr DESPUES de ambas migraciones anteriores.
--
--  ESTA MIGRACION NO MODIFICA EL ESQUEMA: no hace ALTER, ni
--  CREATE/DROP de columnas, constraints, indices, RPCs ni
--  policies. SOLO hace UPDATE de datos y SELECT de verificacion.
--  El swap de UNIQUE de online_clients es de OTRA migracion (PR5);
--  aca no se toca.
--
--  PRINCIPIOS (correccion 5 - backfill):
--    * NUNCA fabrica atribucion. Lo que no es derivable queda NULL.
--    * NUNCA aborta el batch: ningun UPDATE puede romper por dato
--      vacio, tipo, o violacion de unicidad (son UPDATE de columnas
--      nullable sin constraints unicas sobre ellas).
--    * Todo UPDATE esta gateado por (columna IS NULL) -> re-corrible
--      sin reprocesar filas ya atribuidas ni pisar atribucion previa.
--    * section_id se atribuye EXACTO (section TEXT era la wire key,
--      = sections.code). evaluation_id via assignment_id tambien es
--      EXACTO. El resto (ventana de fechas) es BEST-EFFORT y queda
--      como TEMPLATE para que el docente lo complete, porque las
--      evaluaciones NO tienen columnas de ventana (start/end).
-- ============================================================

-- ============================================================
--  0. PARAMETRO DE CURSO
--  section TEXT (001D/002D/003D) es unico SOLO por curso
--  (sections UNIQUE(course_id, code)). Para que el match
--  section TEXT -> sections.id sea deterministico y no choque
--  con otro curso que comparta el code, scopeamos por el curso
--  default FPY1101. Si en el futuro hay datos legacy de otros
--  cursos, duplicar los bloques cambiando el code del curso.
-- ============================================================

-- ============================================================
--  1. BACKFILL EXACTO de section_id (deterministico)
--
--  section TEXT fue la wire key historica, igual a sections.code.
--  El match es exacto. Scopeado al curso FPY1101 para resolver
--  la ambiguedad cross-curso del code. Gateado por
--  section_id IS NULL: solo toca filas aun sin atribuir.
--  cheat_events queda fuera: no tiene ni section ni section_id.
-- ============================================================

-- assignment_acceptances.section_id
UPDATE public.assignment_acceptances AS t
SET section_id = s.id
FROM public.sections s
JOIN public.courses c ON c.id = s.course_id
WHERE t.section_id IS NULL
  AND t.section IS NOT NULL
  AND s.code = t.section
  AND c.code = 'FPY1101';

-- student_activity.section_id
UPDATE public.student_activity AS t
SET section_id = s.id
FROM public.sections s
JOIN public.courses c ON c.id = s.course_id
WHERE t.section_id IS NULL
  AND t.section IS NOT NULL
  AND s.code = t.section
  AND c.code = 'FPY1101';

-- online_clients.section_id
UPDATE public.online_clients AS t
SET section_id = s.id
FROM public.sections s
JOIN public.courses c ON c.id = s.course_id
WHERE t.section_id IS NULL
  AND t.section IS NOT NULL
  AND s.code = t.section
  AND c.code = 'FPY1101';

-- process_alerts.section_id
UPDATE public.process_alerts AS t
SET section_id = s.id
FROM public.sections s
JOIN public.courses c ON c.id = s.course_id
WHERE t.section_id IS NULL
  AND t.section IS NOT NULL
  AND s.code = t.section
  AND c.code = 'FPY1101';

-- browser_history.section_id
UPDATE public.browser_history AS t
SET section_id = s.id
FROM public.sections s
JOIN public.courses c ON c.id = s.course_id
WHERE t.section_id IS NULL
  AND t.section IS NOT NULL
  AND s.code = t.section
  AND c.code = 'FPY1101';

-- ============================================================
--  2. BACKFILL EXACTO de evaluation_id via assignment_id
--
--  SOLO assignment_acceptances tiene assignment_id, asi que es
--  la unica tabla con atribucion EXACTA de evaluation_id (no
--  best-effort). Se copia assignments.evaluation_id a la fila
--  de aceptacion que referencia ese assignment.
--
--  GATE: depende de que assignments.evaluation_id este poblado.
--  Si ningun assignment tiene evaluation_id, este UPDATE es un
--  no-op SILENCIOSO que enmascararia el problema -> primero
--  contamos y emitimos un NOTICE explicito para que el docente
--  sepa que el prerequisito no esta listo, en vez de "exito" falso.
-- ============================================================

DO $$
DECLARE
  v_assignments_con_eval INT;
  v_aa_actualizadas INT;
BEGIN
  SELECT COUNT(*) INTO v_assignments_con_eval
  FROM public.assignments
  WHERE evaluation_id IS NOT NULL;

  IF v_assignments_con_eval = 0 THEN
    RAISE NOTICE 'GATE evaluation_id via assignment_id: 0 assignments tienen evaluation_id. '
                 'NO se backfillea assignment_acceptances.evaluation_id (no hay de donde). '
                 'Asignar evaluation_id a los assignments en el panel y re-correr esta migracion.';
  ELSE
    UPDATE public.assignment_acceptances AS aa
    SET evaluation_id = a.evaluation_id
    FROM public.assignments a
    WHERE aa.assignment_id = a.id
      AND aa.evaluation_id IS NULL
      AND a.evaluation_id IS NOT NULL;

    GET DIAGNOSTICS v_aa_actualizadas = ROW_COUNT;
    RAISE NOTICE 'evaluation_id via assignment_id: % assignments con evaluation_id; '
                 '% filas de assignment_acceptances atribuidas.',
                 v_assignments_con_eval, v_aa_actualizadas;
  END IF;
END $$;

-- ============================================================
--  3. BACKFILL BEST-EFFORT de evaluation_id por VENTANA DE FECHAS
--     (TEMPLATE - requiere ventanas provistas por el docente)
--
--  En el modelo del sistema las evaluaciones son POR SECCION.
--  La forma de atribuir un evento a una evaluacion es: el evento
--  ocurrio en la seccion S (section_id) y su timestamp cae dentro
--  de la ventana de corrida de la evaluacion E de esa seccion.
--
--  PROBLEMA: la tabla `evaluations` NO tiene columnas de ventana
--  (no hay start/end). Por lo tanto NO se puede derivar la ventana
--  desde la DB y NO se debe adivinar. Este bloque queda como
--  TEMPLATE para que el docente complete los rangos reales de cada
--  evaluacion y descomente los UPDATE.
--
--  Cada UPDATE usa la columna de timestamp CORRECTA por tabla
--  (verificadas contra el esquema):
--    process_alerts   -> detected_at
--    browser_history  -> visited_at
--    student_activity -> created_at
--    online_clients   -> last_seen
--  y esta gateado por evaluation_id IS NULL (re-corrible).
--
--  INSTRUCCIONES PARA EL DOCENTE:
--    1. Reemplazar :p_section_code por la seccion ('001D'/'002D'/'003D').
--    2. Reemplazar :p_eval_title por el title de la evaluacion.
--    3. Reemplazar :p_ventana_inicio / :p_ventana_fin por el rango
--       real (TIMESTAMPTZ) en que corrio esa evaluacion en esa seccion.
--    4. Descomentar el/los UPDATE de las tablas que correspondan.
--    5. Repetir el bloque por cada (seccion, evaluacion) a atribuir.
--
--  NOTA: el rango es semiabierto [inicio, fin) para no solapar dos
--  evaluaciones contiguas. Si dos evaluaciones de la MISMA seccion
--  se solaparan en el tiempo, el evento es AMBIGUO y NO se debe
--  atribuir: ajustar las ventanas para que no se solapen, o dejar
--  esas filas en NULL.
-- ============================================================

/*  ===== TEMPLATE: copiar y completar por (seccion, evaluacion) =====

-- Resuelve la evaluacion objetivo (seccion + title) a un id concreto.
-- Si no resuelve a exactamente 1 fila, los UPDATE no tocan nada
-- (subquery vacia o multiple -> el WHERE evaluation_id = (...) no matchea
-- de forma segura; usar el patron con CTE de abajo para fallar visible).

WITH target AS (
  SELECT e.id AS evaluation_id, e.section_id
  FROM public.evaluations e
  JOIN public.sections s ON s.id = e.section_id
  JOIN public.courses  c ON c.id = s.course_id
  WHERE c.code = 'FPY1101'
    AND s.code = '001D'                 -- <-- :p_section_code
    AND e.title = 'Evaluacion-4'        -- <-- :p_eval_title
)
-- process_alerts (detected_at)
UPDATE public.process_alerts AS t
SET evaluation_id = tg.evaluation_id
FROM target tg
WHERE t.evaluation_id IS NULL
  AND t.section_id = tg.section_id
  AND t.detected_at >= TIMESTAMPTZ '2026-06-20 08:00:00-04'   -- <-- :p_ventana_inicio
  AND t.detected_at <  TIMESTAMPTZ '2026-06-20 10:00:00-04';  -- <-- :p_ventana_fin

WITH target AS (
  SELECT e.id AS evaluation_id, e.section_id
  FROM public.evaluations e
  JOIN public.sections s ON s.id = e.section_id
  JOIN public.courses  c ON c.id = s.course_id
  WHERE c.code = 'FPY1101'
    AND s.code = '001D'
    AND e.title = 'Evaluacion-4'
)
-- browser_history (visited_at)
UPDATE public.browser_history AS t
SET evaluation_id = tg.evaluation_id
FROM target tg
WHERE t.evaluation_id IS NULL
  AND t.section_id = tg.section_id
  AND t.visited_at >= TIMESTAMPTZ '2026-06-20 08:00:00-04'
  AND t.visited_at <  TIMESTAMPTZ '2026-06-20 10:00:00-04';

WITH target AS (
  SELECT e.id AS evaluation_id, e.section_id
  FROM public.evaluations e
  JOIN public.sections s ON s.id = e.section_id
  JOIN public.courses  c ON c.id = s.course_id
  WHERE c.code = 'FPY1101'
    AND s.code = '001D'
    AND e.title = 'Evaluacion-4'
)
-- student_activity (created_at)
UPDATE public.student_activity AS t
SET evaluation_id = tg.evaluation_id
FROM target tg
WHERE t.evaluation_id IS NULL
  AND t.section_id = tg.section_id
  AND t.created_at >= TIMESTAMPTZ '2026-06-20 08:00:00-04'
  AND t.created_at <  TIMESTAMPTZ '2026-06-20 10:00:00-04';

WITH target AS (
  SELECT e.id AS evaluation_id, e.section_id
  FROM public.evaluations e
  JOIN public.sections s ON s.id = e.section_id
  JOIN public.courses  c ON c.id = s.course_id
  WHERE c.code = 'FPY1101'
    AND s.code = '001D'
    AND e.title = 'Evaluacion-4'
)
-- online_clients (last_seen)
UPDATE public.online_clients AS t
SET evaluation_id = tg.evaluation_id
FROM target tg
WHERE t.evaluation_id IS NULL
  AND t.section_id = tg.section_id
  AND t.last_seen >= TIMESTAMPTZ '2026-06-20 08:00:00-04'
  AND t.last_seen <  TIMESTAMPTZ '2026-06-20 10:00:00-04';

    ===== FIN TEMPLATE ===== */

-- ============================================================
--  4. BACKFILL BEST-EFFORT de cheat_events.evaluation_id
--
--  cheat_events NO tiene section, section_id ni github_username:
--  tiene `username`, `pc_name` y `detected_at` (PR1 le agrego
--  evaluation_id nullable). No es derivable de forma directa.
--
--  HEURISTICA (best-effort, baja-media confianza): inferir la
--  seccion del evento cruzando username/pc_name contra
--  online_clients y student_activity DENTRO de una ventana de
--  tiempo ajustada alrededor de detected_at, y tomar la seccion
--  SOLO si todas las pistas coinciden en una unica seccion. Una
--  vez inferida la seccion, la atribucion a una evaluacion sigue
--  necesitando la ventana de fechas del bloque 3 (TEMPLATE), por
--  lo que aca NO se setea evaluation_id directo: se documenta el
--  camino y se deja como TEMPLATE para combinar con la ventana.
--
--  REGLA DURA: filas ambiguas (sin pista, o con pistas que apuntan
--  a >1 seccion) QUEDAN EN NULL. NUNCA se fabrica atribucion.
--
--  LIMITE DE CONFIANZA: el match username/pc_name + ventana es
--  una INFERENCIA, no un hecho. El docente debe revisarlo antes
--  de tratar estas filas como atribuidas con certeza. Por eso el
--  UPDATE queda comentado como TEMPLATE; la parte automatica solo
--  REPORTA cuantas filas serian inferibles vs cuantas quedan
--  genuinamente sin pista.
-- ============================================================

-- Diagnostico no-destructivo: cuantos cheat_events tienen al menos
-- una pista de seccion UNICA (username o pc_name -> una sola seccion)
-- dentro de una ventana de +/- 1 hora alrededor de detected_at.
DO $$
DECLARE
  v_total INT;
  v_sin_eval INT;
  v_inferibles INT;
BEGIN
  SELECT COUNT(*) INTO v_total FROM public.cheat_events;
  SELECT COUNT(*) INTO v_sin_eval
    FROM public.cheat_events WHERE evaluation_id IS NULL;

  SELECT COUNT(*) INTO v_inferibles
  FROM public.cheat_events ce
  WHERE ce.evaluation_id IS NULL
    AND (
      SELECT COUNT(DISTINCT src.section_id) = 1
      FROM (
        SELECT oc.section_id
        FROM public.online_clients oc
        WHERE oc.section_id IS NOT NULL
          AND (oc.github_username = ce.username OR oc.pc_name = ce.pc_name)
          AND oc.last_seen BETWEEN ce.detected_at - INTERVAL '1 hour'
                               AND ce.detected_at + INTERVAL '1 hour'
        UNION ALL
        SELECT sa.section_id
        FROM public.student_activity sa
        WHERE sa.section_id IS NOT NULL
          AND (sa.github_username = ce.username OR sa.pc_name = ce.pc_name)
          AND sa.created_at BETWEEN ce.detected_at - INTERVAL '1 hour'
                                AND ce.detected_at + INTERVAL '1 hour'
      ) src
    );

  RAISE NOTICE 'cheat_events: % total; % sin evaluation_id; '
               '% con seccion inferible UNICA (pista username/pc_name +/-1h). '
               'La atribucion a evaluacion exige la ventana del bloque 3 (TEMPLATE); '
               'las filas ambiguas o sin pista QUEDAN EN NULL (no se fabrica).',
               v_total, v_sin_eval, v_inferibles;
END $$;

/*  ===== TEMPLATE cheat_events: combinar seccion inferida + ventana =====
    Completar :p_section_code, :p_eval_title, :p_ventana_inicio, :p_ventana_fin
    igual que el bloque 3. Solo atribuye filas cuya seccion inferida (pista
    UNICA) coincide con la seccion de la evaluacion Y cuyo detected_at cae en
    la ventana. Las ambiguas (DISTINCT section_id <> 1) quedan en NULL.

WITH target AS (
  SELECT e.id AS evaluation_id, e.section_id
  FROM public.evaluations e
  JOIN public.sections s ON s.id = e.section_id
  JOIN public.courses  c ON c.id = s.course_id
  WHERE c.code = 'FPY1101'
    AND s.code = '001D'                 -- <-- :p_section_code
    AND e.title = 'Evaluacion-4'        -- <-- :p_eval_title
),
inferida AS (
  SELECT ce.id AS cheat_id, MIN(src.section_id) AS section_id
  FROM public.cheat_events ce
  JOIN LATERAL (
    SELECT oc.section_id
    FROM public.online_clients oc
    WHERE oc.section_id IS NOT NULL
      AND (oc.github_username = ce.username OR oc.pc_name = ce.pc_name)
      AND oc.last_seen BETWEEN ce.detected_at - INTERVAL '1 hour'
                           AND ce.detected_at + INTERVAL '1 hour'
    UNION ALL
    SELECT sa.section_id
    FROM public.student_activity sa
    WHERE sa.section_id IS NOT NULL
      AND (sa.github_username = ce.username OR sa.pc_name = ce.pc_name)
      AND sa.created_at BETWEEN ce.detected_at - INTERVAL '1 hour'
                            AND ce.detected_at + INTERVAL '1 hour'
  ) src ON TRUE
  WHERE ce.evaluation_id IS NULL
  GROUP BY ce.id
  HAVING COUNT(DISTINCT src.section_id) = 1   -- pista UNICA; ambiguas excluidas
)
UPDATE public.cheat_events AS t
SET evaluation_id = tg.evaluation_id
FROM inferida inf
JOIN target tg ON tg.section_id = inf.section_id
WHERE t.id = inf.cheat_id
  AND t.evaluation_id IS NULL
  AND t.detected_at >= TIMESTAMPTZ '2026-06-20 08:00:00-04'   -- <-- :p_ventana_inicio
  AND t.detected_at <  TIMESTAMPTZ '2026-06-20 10:00:00-04';  -- <-- :p_ventana_fin

    ===== FIN TEMPLATE cheat_events ===== */

-- ============================================================
--  5. VERIFICACION
--  Reporta, por tabla, cuantas filas quedan SIN section_id y
--  SIN evaluation_id tras el backfill. Asi el docente ve el
--  remanente exacto NO atribuido y puede distinguir lo que es
--  genuinamente fuera-de-ventana (esperado) de un bug. Ningun
--  SELECT aqui puede abortar el batch.
-- ============================================================

SELECT
  'assignment_acceptances' AS tabla,
  COUNT(*)                                          AS total,
  COUNT(*) FILTER (WHERE section_id IS NULL)         AS sin_section_id,
  COUNT(*) FILTER (WHERE evaluation_id IS NULL)      AS sin_evaluation_id
FROM public.assignment_acceptances
UNION ALL
SELECT
  'student_activity',
  COUNT(*),
  COUNT(*) FILTER (WHERE section_id IS NULL),
  COUNT(*) FILTER (WHERE evaluation_id IS NULL)
FROM public.student_activity
UNION ALL
SELECT
  'online_clients',
  COUNT(*),
  COUNT(*) FILTER (WHERE section_id IS NULL),
  COUNT(*) FILTER (WHERE evaluation_id IS NULL)
FROM public.online_clients
UNION ALL
SELECT
  'process_alerts',
  COUNT(*),
  COUNT(*) FILTER (WHERE section_id IS NULL),
  COUNT(*) FILTER (WHERE evaluation_id IS NULL)
FROM public.process_alerts
UNION ALL
SELECT
  'browser_history',
  COUNT(*),
  COUNT(*) FILTER (WHERE section_id IS NULL),
  COUNT(*) FILTER (WHERE evaluation_id IS NULL)
FROM public.browser_history
UNION ALL
-- cheat_events NO tiene section_id (no aplica): se reporta NULL.
SELECT
  'cheat_events',
  COUNT(*),
  NULL,
  COUNT(*) FILTER (WHERE evaluation_id IS NULL)
FROM public.cheat_events
ORDER BY tabla;
