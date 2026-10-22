-- Topes que la base controla sola (revisión del 2026-10-22).
--
-- La web y la app revisan cada BO antes de guardarla (parseBuildOrder: 512 KB, hasta 300 pasos, count y builders
-- de 1 a 200), pero cualquiera con una cuenta puede hablarle directo a Supabase con su sesión (insert en
-- build_orders) o con su token (rpc app_publish) y saltearse esa revisión. Sin estos topes, una BO de decenas de
-- megas la bajaría cada visita a su página, el sitemap y la app de quien la importe.
--
-- Las restricciones nuevas van NOT VALID: se controlan en cada insert y update, sin revisar las BO que ya están.

-- count y builders de un paso: ausentes, null o un entero de 1 a 200 (como BuildOrderValidator.MaxCount).
create function public.step_number_ok(value jsonb) returns boolean
language sql immutable set search_path = '' as $$
  select case jsonb_typeof(value)
           when 'number' then (value::text)::numeric between 1 and 200 and (value::text)::numeric = trunc((value::text)::numeric)
           when 'null' then true
           else value is null
         end
$$;

-- Los pasos de una BO o de una rama: una lista de 1 a 300 objetos con count y builders dentro del tope.
create function public.steps_ok(steps jsonb) returns boolean
language sql immutable set search_path = '' as $$
  select case
           when jsonb_typeof(steps) is distinct from 'array' then false
           when jsonb_array_length(steps) not between 1 and 300 then false
           else not exists (
             select 1
               from jsonb_array_elements(steps) as s(step)
              where jsonb_typeof(step) <> 'object'
                 or not public.step_number_ok(step -> 'count')
                 or not public.step_number_ok(step -> 'builders'))
         end
$$;

-- La BO entera: hasta 512 000 bytes, sus pasos y los de cada rama (hasta 20 ramas).
create function public.build_order_body_ok(body jsonb) returns boolean
language sql immutable set search_path = '' as $$
  select octet_length(body::text) <= 512000
     and public.steps_ok(body -> 'steps')
     and case
           when body -> 'branches' is null or jsonb_typeof(body -> 'branches') = 'null' then true
           when jsonb_typeof(body -> 'branches') <> 'array' or jsonb_array_length(body -> 'branches') > 20 then false
           else not exists (
             select 1
               from jsonb_array_elements(body -> 'branches') as b(branch)
              where jsonb_typeof(branch) <> 'object' or not public.steps_ok(branch -> 'steps'))
         end
$$;

-- Etiquetas de hasta 30 caracteres (como el esquema de la web).
create function public.tags_ok(tags text[]) returns boolean
language sql immutable set search_path = '' as $$
  select coalesce(bool_and(char_length(tag) between 1 and 30), true) from unnest(tags) as t(tag)
$$;

alter table public.build_orders
  add constraint build_orders_body_ok check (public.build_order_body_ok(body)) not valid,
  add constraint build_orders_tags_ok check (public.tags_ok(tags)) not valid,
  add constraint build_orders_schema_version_ok check (schema_version between 1 and 100) not valid,
  add constraint build_orders_clicks_ok check (
    (feudal_click is null or feudal_click between 0 and 86400) and
    (castle_click is null or castle_click between 0 and 86400)) not valid;

alter table public.profiles
  add constraint profiles_avatar_url_ok check (avatar_url is null or char_length(avatar_url) <= 1000) not valid;

-- ---------------------------------------------------------------------------------------------------------------
-- Contra el spam: hasta 150 BO nuevas por cuenta cada 24 horas (alcanza para subir una por civ de una vez) y
-- hasta 20 tokens de la app por cuenta.
-- ---------------------------------------------------------------------------------------------------------------
create function public.limit_new_build_orders() returns trigger
language plpgsql security definer set search_path = '' as $$
begin
  if (select count(*) from public.build_orders
       where owner_id = new.owner_id and created_at > now() - interval '24 hours') >= 150 then
    raise exception 'Publicaste muchas build orders en las últimas 24 horas: probá de nuevo mañana.' using errcode = '23514';
  end if;
  return new;
end $$;

create trigger build_orders_limit
  before insert on public.build_orders
  for each row execute function public.limit_new_build_orders();

create index build_orders_owner_created on public.build_orders (owner_id, created_at desc);

create or replace function public.create_app_token(p_label text default 'BO Coach') returns text
language plpgsql security definer set search_path = '' as $$
declare
  token text := 'bocoach_' || replace(gen_random_uuid()::text || gen_random_uuid()::text, '-', '');
begin
  if auth.uid() is null then
    raise exception 'Hace falta iniciar sesión.' using errcode = '42501';
  end if;
  if (select count(*) from public.app_tokens where user_id = auth.uid()) >= 20 then
    raise exception 'Ya tenés 20 tokens: borrá alguno que no uses antes de crear otro.' using errcode = '23514';
  end if;
  insert into public.app_tokens (user_id, token_hash, label)
  values (auth.uid(), public.hash_token(token), coalesce(nullif(trim(p_label), ''), 'BO Coach'));
  return token;
end $$;

-- Las funciones internas no se llaman desde afuera (las de los checks las usa la base al guardar; new_short_code
-- queda abierta porque es el valor por defecto de la columna y corre con el rol de quien inserta).
revoke execute on function public.hash_token(text) from public, anon, authenticated;
revoke execute on function public.limit_new_build_orders() from public, anon, authenticated;
