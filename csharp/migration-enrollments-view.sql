-- ============================================================
--  Migracion: v_enrollment_status (vista de cruce roster vs actividad)
--  Idempotente. Correr en Supabase SQL Editor.
--
--  Capa de SOLO LECTURA encima de:
--    - enrollments               (roster importado; migration-enrollments.sql)
--    - assignment_acceptances    (acepto la tarea; migration-acceptances.sql)
--    - assignment_submissions    (entrego el repo; migration-submissions.sql)
--    - sections                  (resolucion de seccion; migration-multi-evaluation.sql)
--
--  ORDEN DE EJECUCION: correr DESPUES de migration-enrollments.sql,
--  migration-acceptances.sql, migration-submissions.sql y
--  migration-multi-evaluation.sql (todas proveen los objetos que la
--  vista referencia).
--
--  Proposito: dar al panel docente una unica fuente de verdad para
--  validar el roster contra la actividad real, SIN joins cross-table del
--  lado cliente. La vista cruza por github (case-insensitive) y emite,
--  por fila, si el github fue resuelto, si acepto y si entrego.
--
--  Diseno del cruce (github-first):
--    - Lado ROSTER: cada inscripcion 'enrolled' es una fila. Si tiene
--      github, se busca su actividad (acceptances/submissions) por
--      lower(github). Las inscripciones sin github son la cola de
--      "falta asignar github".
--    - Lado ORPHAN: cada github visto en la actividad que NO matchea
--      ninguna inscripcion de SU seccion es un huerfano (github fantasma:
--      actividad sin roster que la respalde).
--
--  Resolucion de seccion (COALESCE) para el lado ORPHAN: la actividad
--  legacy puede traer section_id NULL (cliente viejo que solo manda
--  section TEXT antes de que el trigger sync_section_id lo complete, o
--  un code que no matchea ninguna seccion). Se resuelve
--  COALESCE(a.section_id, s_by_code.id). Si AUN asi queda NULL, la fila
--  NO es huerfana: va al bucket separado 'seccion sin resolver'
--  (source = 'unresolved_section'), porque sin seccion no se la puede
--  contrastar contra ningun roster.
--
--  NO destructivo: solo CREATE OR REPLACE VIEW + GRANT SELECT. No toca
--  tablas ni datos. Re-corrible.
-- ============================================================

-- ============================================================
--  v_enrollment_status
--  Columnas (estables; el panel mapea EnrollmentStatusRow):
--    source            'roster' | 'orphan' | 'unresolved_section'
--    enrollment_id     id de enrollments (NULL en orphan/unresolved)
--    section_id        seccion resuelta (NULL solo en 'unresolved_section')
--    full_name         PII; NULL en orphan/unresolved (no hay roster)
--    email             PII; NULL en orphan/unresolved
--    github_username   github de la inscripcion o de la actividad huerfana
--    status            status de la inscripcion (NULL fuera de 'roster')
--    github_resolved   TRUE si la fila tiene un github no-NULL
--    accepted          TRUE si ese github acepto al menos una tarea
--    submitted         TRUE si ese github entrego al menos un repo
--
--  Es una vista RLS-aware: corre con los privilegios del caller. El
--  panel docente (authenticated) puede leer enrollments; por eso la
--  vista expone PII (full_name/email) SOLO para el rol authenticated.
--  El rol anon NO recibe GRANT (ver seccion de GRANT abajo).
-- ============================================================

CREATE OR REPLACE VIEW public.v_enrollment_status AS
WITH
-- Un github distinto por seccion presente en la actividad, con la
-- seccion resuelta via COALESCE(section_id, lookup por code). Sirve
-- tanto para detectar huerfanos como para clasificar 'seccion sin
-- resolver'. lower(github) es la clave de cruce.
activity_github AS (
  SELECT
    lower(a.github_username) AS github_lower,
    a.github_username        AS github_username,
    COALESCE(a.section_id, s_by_code.id) AS section_id,
    bool_or(TRUE)            AS accepted,
    bool_or(sub.id IS NOT NULL) AS submitted
  FROM public.assignment_acceptances a
  LEFT JOIN public.sections s_by_code
    ON a.section_id IS NULL
   AND a.section IS NOT NULL
   AND s_by_code.code = a.section
  LEFT JOIN public.assignment_submissions sub
    ON lower(sub.github_username) = lower(a.github_username)
  WHERE a.github_username IS NOT NULL
  GROUP BY
    lower(a.github_username),
    a.github_username,
    COALESCE(a.section_id, s_by_code.id)
),
-- Igual que arriba pero desde submissions, para capturar github que
-- entrego sin tener una fila de acceptance (entrega manual directa).
-- submissions no tiene seccion propia: se hereda de la acceptance del
-- mismo github si existe; si no, queda NULL -> 'seccion sin resolver'.
submission_github AS (
  SELECT
    lower(sub.github_username) AS github_lower,
    sub.github_username        AS github_username,
    ag.section_id              AS section_id,
    COALESCE(ag.accepted, FALSE) AS accepted,
    TRUE                       AS submitted
  FROM public.assignment_submissions sub
  LEFT JOIN activity_github ag
    ON ag.github_lower = lower(sub.github_username)
  WHERE sub.github_username IS NOT NULL
),
-- Union de actividad por (github_lower, section_id). Colapsa acceptance
-- y submission del mismo github/seccion en una sola fila de actividad.
activity AS (
  SELECT
    github_lower,
    max(github_username) AS github_username,
    section_id,
    bool_or(accepted)  AS accepted,
    bool_or(submitted) AS submitted
  FROM (
    SELECT github_lower, github_username, section_id, accepted, submitted FROM activity_github
    UNION ALL
    SELECT github_lower, github_username, section_id, accepted, submitted FROM submission_github
  ) u
  GROUP BY github_lower, section_id
)

-- ----------------------------------------------------------------
-- Lado ROSTER: una fila por inscripcion 'enrolled'. accepted/submitted
-- se cruzan por (lower(github), section_id) contra la actividad. Las
-- inscripciones sin github tienen github_resolved=FALSE y accepted/
-- submitted en FALSE (no hay con que cruzar): son la cola de "falta
-- asignar github".
-- ----------------------------------------------------------------
SELECT
  'roster'::text                       AS source,
  e.id                                 AS enrollment_id,
  e.section_id                         AS section_id,
  e.full_name                          AS full_name,
  e.email                              AS email,
  e.github_username                    AS github_username,
  e.status                             AS status,
  (e.github_username IS NOT NULL)      AS github_resolved,
  COALESCE(act.accepted, FALSE)        AS accepted,
  COALESCE(act.submitted, FALSE)       AS submitted
FROM public.enrollments e
LEFT JOIN activity act
  ON e.github_username IS NOT NULL
 AND act.github_lower = lower(e.github_username)
 AND act.section_id  IS NOT DISTINCT FROM e.section_id
WHERE e.status = 'enrolled'

UNION ALL

-- ----------------------------------------------------------------
-- Lado ORPHAN: github con actividad en una seccion RESUELTA que NO
-- matchea ninguna inscripcion 'enrolled' de esa misma seccion. Es un
-- github fantasma (actividad sin roster). source='orphan'.
-- ----------------------------------------------------------------
SELECT
  'orphan'::text          AS source,
  NULL::bigint            AS enrollment_id,
  act.section_id          AS section_id,
  NULL::text              AS full_name,
  NULL::text              AS email,
  act.github_username     AS github_username,
  NULL::text              AS status,
  TRUE                    AS github_resolved,
  act.accepted            AS accepted,
  act.submitted           AS submitted
FROM activity act
WHERE act.section_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM public.enrollments e
    WHERE e.status = 'enrolled'
      AND e.github_username IS NOT NULL
      AND lower(e.github_username) = act.github_lower
      AND e.section_id = act.section_id
  )

UNION ALL

-- ----------------------------------------------------------------
-- Bucket SEPARADO 'seccion sin resolver': actividad cuya seccion no se
-- pudo resolver (section_id NULL tras el COALESCE). NO es huerfana: sin
-- seccion no se la puede contrastar contra ningun roster. Se mantiene
-- aparte para que el panel no la mezcle con los huerfanos reales.
-- ----------------------------------------------------------------
SELECT
  'unresolved_section'::text AS source,
  NULL::bigint               AS enrollment_id,
  NULL::bigint               AS section_id,
  NULL::text                 AS full_name,
  NULL::text                 AS email,
  act.github_username        AS github_username,
  NULL::text                 AS status,
  TRUE                       AS github_resolved,
  act.accepted               AS accepted,
  act.submitted              AS submitted
FROM activity act
WHERE act.section_id IS NULL;

-- ============================================================
--  GRANT
--  La vista expone PII (full_name/email del lado roster). Solo el rol
--  authenticated (panel docente) recibe SELECT. anon NO: mantiene la
--  garantia de enrollments (primera tabla PII, authenticated-only). La
--  vista NO es SECURITY DEFINER: respeta la RLS del caller, asi que
--  aunque alguien forzara el GRANT a anon, la RLS de enrollments igual
--  bloquearia el lado roster.
-- ============================================================

REVOKE ALL ON public.v_enrollment_status FROM anon;
GRANT SELECT ON public.v_enrollment_status TO authenticated;

-- ============================================================
--  VERIFICACION
-- ============================================================

SELECT source, COUNT(*) AS filas
FROM public.v_enrollment_status
GROUP BY source
ORDER BY source;
