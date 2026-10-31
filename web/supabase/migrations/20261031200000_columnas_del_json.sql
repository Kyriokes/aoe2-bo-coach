-- Las columnas de cada BO salen de su JSON (auditoría del 2026-10-31, M2).
--
-- La web guardaba nombre, civ, etiquetas y descripción en columnas y además el JSON entero en body. Con una sesión o
-- un token se le puede hablar directo a PostgREST y mandar un body que no coincida con las columnas (una BO listada
-- como de una civ y simulada como de otra), y el autor que dice el JSON (body->>'author') no tenía tope: la web y la
-- app lo muestran tal cual. Ahora un trigger deriva las columnas del JSON en cada insert y update (lo que se mande en
-- esas columnas se ignora), así que los topes de las columnas valen también para el JSON, y el autor tiene hasta 60
-- caracteres como en el esquema de la web.
--
-- Se corre una vez en el SQL Editor de Supabase.

create function public.sync_build_order_columns() returns trigger
language plpgsql set search_path = '' as $$
declare
  version numeric;
begin
  if jsonb_typeof(new.body -> 'schema_version') is distinct from 'number' then
    raise exception 'La BO tiene que traer schema_version (un número entero).' using errcode = '23514';
  end if;
  version := (new.body ->> 'schema_version')::numeric;
  if version <> trunc(version) then
    raise exception 'La BO tiene que traer schema_version (un número entero).' using errcode = '23514';
  end if;
  if jsonb_typeof(new.body -> 'name') is distinct from 'string' or trim(new.body ->> 'name') = '' then
    raise exception 'La BO tiene que traer un nombre.' using errcode = '23514';
  end if;

  new.schema_version := version::int;
  new.name := trim(new.body ->> 'name');
  new.civ := coalesce(nullif(trim(new.body ->> 'civ'), ''), 'any');
  new.tags := case
                when jsonb_typeof(new.body -> 'tags') = 'array' then
                  coalesce(array(select trim(t) from jsonb_array_elements_text(new.body -> 'tags') as e(t) where trim(t) <> ''), '{}')
                else '{}'
              end;
  new.description := nullif(trim(new.body ->> 'description'), '');
  return new;
end $$;

-- Antes que build_orders_limit y build_orders_touch (van por nombre): las restricciones se revisan después de todos.
create trigger build_orders_columns
  before insert or update on public.build_orders
  for each row execute function public.sync_build_order_columns();

revoke execute on function public.sync_build_order_columns() from public, anon, authenticated;

alter table public.build_orders
  add constraint build_orders_author_ok check (char_length(coalesce(body ->> 'author', '')) <= 60) not valid;
