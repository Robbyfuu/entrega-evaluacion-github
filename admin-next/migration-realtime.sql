-- ============================================================================
-- Realtime para el Panel del Profesor (admin-next)
-- ----------------------------------------------------------------------------
-- EJECUTAR UNA VEZ en Supabase: Dashboard -> SQL Editor -> New query -> Run.
-- Habilita las publicaciones de Realtime sobre las tablas que el panel
-- monitorea en vivo. Es idempotente: re-ejecutarlo no produce errores aunque
-- una tabla ya esté en la publicación.
-- ============================================================================

do $$
declare
  t text;
  tables text[] := array[
    'control',
    'online_clients',
    'browser_history',
    'cheat_events',
    'process_alerts'
  ];
begin
  foreach t in array tables loop
    if not exists (
      select 1
      from pg_publication_tables
      where pubname = 'supabase_realtime'
        and schemaname = 'public'
        and tablename = t
    ) then
      execute format('alter publication supabase_realtime add table public.%I', t);
      raise notice 'Realtime habilitado para public.%', t;
    else
      raise notice 'public.% ya estaba en supabase_realtime (sin cambios)', t;
    end if;
  end loop;
end $$;
