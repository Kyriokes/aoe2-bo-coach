-- Revisión del 2026-11-05 (docs/auditorias/2026-11-05.md, sección BD): lo que hacía falta antes de abrir la beta.
--
-- Se corre una vez en el SQL Editor de Supabase, entera (va toda en una transacción: si algo falla, no queda nada a
-- medias), y se puede volver a correr sin romper nada. Después de esto se pide el push de la web; la web publicada
-- sigue andando con esta base mientras tanto.
--
-- ---------------------------------------------------------------------------------------------------------------
-- Registros de partida.
-- ---------------------------------------------------------------------------------------------------------------

-- BD-01: done y forget llamaban a record_install, que crea la instalación: sin cuenta y con identidades al azar se
-- llenaba record_installs y, con eso, la base. Ahora solo la reserva crea (y tiene topes); los demás solo verifican.

-- True si la instalación existe y el secreto es el suyo, false si no existe. Error 28000 si el secreto no coincide.
create or replace function public.record_install_known(p_install text, p_secret text) returns boolean
language plpgsql security definer set search_path = '' as $$
declare
  found_hash text;
begin
  if p_install is null or p_install !~ '^[0-9a-f]{32}$' or p_secret is null or p_secret !~ '^[0-9a-f]{64}$' then
    raise exception 'La identidad de la instalación no tiene el formato esperado.' using errcode = '23514';
  end if;
  select secret_hash into found_hash from public.record_installs where id = p_install;
  if not found then
    return false;
  end if;
  if found_hash <> public.hash_token(p_secret) then
    raise exception 'El secreto no es el de esa instalación.' using errcode = '28000';
  end if;
  update public.record_installs set last_seen_at = now() where id = p_install;
  return true;
end $$;

-- BD-04: el tamaño lo decía el que llama: se reservaba 1 byte y se subía 1 MB directo a Storage, y el tope veía 1 byte
-- por archivo. Ahora el tamaño sale de Storage (metadata, que escribe el servidor al guardar el archivo) al marcar subido y
-- al mirar el tope. La subida directa sigue salteando lo que valida la web (cerrarla pide un secreto entre la web y la
-- base, que no hay): quien lea los registros los trata como no confiables.

-- El archivo ya está en Storage. Si no está, error: la app vuelve a intentar más tarde.
create or replace function public.app_record_done(p_install text, p_secret text, p_game text) returns void
language plpgsql security definer set search_path = '' as $$
declare
  real_size bigint;
begin
  if not public.record_install_known(p_install, p_secret) then
    raise exception 'Esa instalación todavía no reservó nada.' using errcode = '28000';
  end if;
  select (o.metadata ->> 'size')::bigint into real_size
    from public.game_records g
    join storage.objects o on o.bucket_id = 'registros' and o.name = g.path
   where g.install_id = p_install and g.game_id = p_game;
  if real_size is null then
    raise exception 'El archivo de ese registro no está en Storage.';
  end if;
  update public.game_records
     set uploaded_at = now(), size = real_size
   where install_id = p_install and game_id = p_game and uploaded_at is null;
end $$;

-- BD-03: las reservas no vencían y contaban aunque no se subiera nada: 900 reservas de 1 MB agotaban el lugar para
-- siempre, y 1000 identidades nuevas, el cupo de cada día. Ahora una reserva dura 24 horas, mientras tanto ocupa lo más
-- que puede pesar (lo que dice p_size lo escribe el que llama) y las instalaciones de menos de un día tienen su propio
-- cupo: un ataque con identidades nuevas no frena a las que ya estaban.
create or replace function public.record_limit(what text) returns bigint
language sql immutable as $$
  select case what
    when 'install_per_day' then 30
    when 'total_per_day' then 1000
    when 'new_install_per_day' then 300
    when 'total_bytes' then 900 * 1024 * 1024
  end::bigint
$$;

-- Reserva el lugar de un registro. { path, status }: "new" = hay que subirlo, "exists" = ya estaba (reenviar no duplica).
-- BD-13: sin un candado, pedidos simultáneos pasaban los topes (cada uno cuenta antes de que el otro inserte). Es uno
-- solo porque hay topes en total; dura lo que tarda esta función y lo toma únicamente una reserva nueva.
create or replace function public.app_record_reserve(p_install text, p_secret text, p_game text, p_size int, p_version text) returns jsonb
language plpgsql security definer set search_path = '' as $$
declare
  existing public.game_records;
  reserved boolean;
  recent boolean;
  new_path text;
begin
  perform public.record_install(p_install, p_secret);
  if p_game is null or p_game !~ '^[0-9a-f]{32}$' then
    raise exception 'El identificador de la partida no tiene el formato esperado.' using errcode = '23514';
  end if;
  if p_size is null or p_size not between 1 and 1048576 then
    raise exception 'El registro tiene que pesar hasta 1 MB.' using errcode = '23514';
  end if;
  if p_version is null or char_length(p_version) not between 1 and 40 then
    raise exception 'La versión de la app tiene hasta 40 caracteres.' using errcode = '23514';
  end if;

  select * into existing from public.game_records where install_id = p_install and game_id = p_game;
  reserved := found;
  if reserved and (existing.uploaded_at is not null or existing.forgotten_at is not null) then
    return jsonb_build_object('path', existing.path, 'status', 'exists');
  end if;
  if reserved and existing.reserved_at > now() - interval '1 day' then
    return jsonb_build_object('path', existing.path, 'status', 'new');
  end if;

  perform pg_advisory_xact_lock(hashtextextended('registros', 0));
  if (select count(*) from public.game_records where install_id = p_install and reserved_at > now() - interval '1 day') >= public.record_limit('install_per_day') then
    raise exception 'Demasiadas partidas de esta instalación hoy.' using errcode = 'BC429';
  end if;
  select i.created_at > now() - interval '1 day' into recent from public.record_installs i where i.id = p_install;
  if (select count(*)
        from public.game_records g
        join public.record_installs i on i.id = g.install_id
       where g.reserved_at > now() - interval '1 day' and (i.created_at > now() - interval '1 day') = recent)
     >= public.record_limit(case when recent then 'new_install_per_day' else 'total_per_day' end) then
    raise exception 'Demasiadas partidas hoy.' using errcode = 'BC429';
  end if;
  -- Lo que hay en Storage, más 1 MB (el tope del bucket) por esta y por cada reserva vigente sin archivo.
  if (select coalesce(sum((o.metadata ->> 'size')::bigint), 0) from storage.objects o where o.bucket_id = 'registros')
     + 1048576 * (1 + (select count(*) from public.game_records g
                        where g.uploaded_at is null and g.forgotten_at is null and g.reserved_at > now() - interval '1 day'
                          and not exists (select 1 from storage.objects o where o.bucket_id = 'registros' and o.name = g.path)))
     > public.record_limit('total_bytes') then
    raise exception 'No hay lugar para más registros.' using errcode = 'BC429';
  end if;

  if reserved then
    update public.game_records
       set reserved_at = now(), size = p_size, app_version = p_version
     where install_id = p_install and game_id = p_game;
    return jsonb_build_object('path', existing.path, 'status', 'new');
  end if;
  new_path := p_install || '/' || p_game || '.jsonl.gz';
  insert into public.game_records (install_id, game_id, path, size, app_version) values (p_install, p_game, new_path, p_size, p_version);
  return jsonb_build_object('path', new_path, 'status', 'new');
end $$;

-- El pedido de borrado: marca lo de la instalación y devuelve todas sus rutas marcadas para borrar los archivos. Si
-- algo falla a mitad de camino, otro pedido las vuelve a devolver. Las filas quedan (sin archivo) para no contar otra
-- vez la misma partida. Una instalación que nunca reservó no tiene nada que borrar.
--
-- BD-08: lo marcado para borrar lo podía leer cualquiera hasta que se borrara y, si el borrado fallaba, para siempre.
-- Storage pide poder ver un archivo para borrarlo, así que se ve solo 2 minutos desde el pedido; cada pedido abre otra
-- vez ese rato para todo lo de la instalación.
create or replace function public.app_record_forget(p_install text, p_secret text) returns text[]
language plpgsql security definer set search_path = '' as $$
declare
  paths text[];
begin
  if not public.record_install_known(p_install, p_secret) then
    return '{}';
  end if;
  update public.game_records set forgotten_at = now() where install_id = p_install;
  select coalesce(array_agg(path order by path), '{}') into paths from public.game_records where install_id = p_install;
  return paths;
end $$;

create or replace function public.record_is_forgotten(p_path text) returns boolean
language sql stable security definer set search_path = '' as $$
  select exists (select 1 from public.game_records where path = p_path and forgotten_at > now() - interval '2 minutes')
$$;

-- Lo que pregunta la política de Storage para subir: una reserva vigente.
create or replace function public.record_is_reserved(p_path text) returns boolean
language sql stable security definer set search_path = '' as $$
  select exists (select 1 from public.game_records
                  where path = p_path and uploaded_at is null and forgotten_at is null and reserved_at > now() - interval '1 day')
$$;

revoke execute on function public.record_install_known(text, text) from public, anon, authenticated;

-- ---------------------------------------------------------------------------------------------------------------
-- Build orders.
-- ---------------------------------------------------------------------------------------------------------------

-- BD-07: votar actualiza build_orders (el contador) y el trigger tocaba updated_at: cambiaba la fecha mostrada y el
-- sitemap, y la app creía que había una versión nueva, la bajaba y reiniciaba el coach en la pantalla de carga, sin
-- ningún cambio. Cualquier cuenta lo podía disparar contra todos los suscriptos. La fecha es la del JSON.
drop trigger if exists build_orders_touch on public.build_orders;
create trigger build_orders_touch
  before update on public.build_orders
  for each row
  when (old.body is distinct from new.body)
  execute function public.touch_updated_at();

-- BD-10: sin tope para after ni para los objetivos, una BO pública de 512 KB con 90.000 after o 20.000 objetivos
-- congelaba la pestaña de quien la abre. Hasta 50 y hasta 40, como el esquema de la web y el validador del Core.
-- BD-16: lo demás que la base no miraba y la web sí: el id y la acción de cada paso (por insert directo quedaba listada
-- una BO que la app no lee) y el mapa (500 KB de mapa viajaban en la lista de cada app). schema_version sigue de 1 a 100
-- a propósito, aunque la web entienda hasta 2: una versión nueva del formato no tiene que tocar la base, y la app rechaza
-- con un mensaje la versión que no conoce.

-- Los after de un paso: ausentes, null o una lista de hasta 50.
create or replace function public.step_after_ok(value jsonb) returns boolean
language sql immutable set search_path = '' as $$
  select case jsonb_typeof(value)
           when 'array' then jsonb_array_length(value) <= 50
           when 'null' then true
           else value is null
         end
$$;

-- Los pasos de una BO o de una rama: una lista de 1 a 300 objetos con su id, una acción de la lista y los números
-- dentro del tope.
create or replace function public.steps_ok(steps jsonb) returns boolean
language sql immutable set search_path = '' as $$
  select case
           when jsonb_typeof(steps) is distinct from 'array' then false
           when jsonb_array_length(steps) not between 1 and 300 then false
           else not exists (
             select 1
               from jsonb_array_elements(steps) as s(step)
              where jsonb_typeof(step) <> 'object'
                 or jsonb_typeof(step -> 'id') is distinct from 'string'
                 or char_length(step ->> 'id') not between 1 and 40
                 or coalesce(step ->> 'do', '') not in ('train', 'build', 'research', 'move')
                 or not public.step_number_ok(step -> 'count')
                 or not public.step_number_ok(step -> 'builders')
                 or not public.step_after_ok(step -> 'after'))
         end
$$;

-- BD-05: una sola cuenta llenaba la base (150 BO de 512 KB por día, y reemplazar en loop dejaba 512 KB muertos cada
-- vez). Ahora: hasta 128 KB por BO (la más grande del repo pesa 12 KB), 20 MB por cuenta (lo que de verdad ocupa cada
-- BO, ya comprimida) y una BO no se reemplaza dos veces en 5 segundos.
create or replace function public.build_order_body_ok(body jsonb) returns boolean
language sql immutable set search_path = '' as $$
  select octet_length(body::text) <= 128000
     and public.steps_ok(body -> 'steps')
     and case
           when body -> 'branches' is null or jsonb_typeof(body -> 'branches') = 'null' then true
           when jsonb_typeof(body -> 'branches') <> 'array' or jsonb_array_length(body -> 'branches') > 20 then false
           else not exists (
             select 1
               from jsonb_array_elements(body -> 'branches') as b(branch)
              where jsonb_typeof(branch) <> 'object' or not public.steps_ok(branch -> 'steps'))
         end
     and case
           when coalesce(jsonb_typeof(body -> 'assisted'), 'null') = 'null' then true
           when jsonb_typeof(body -> 'assisted') <> 'object' then false
           when coalesce(jsonb_typeof(body -> 'assisted' -> 'goals'), 'null') = 'null' then true
           when jsonb_typeof(body -> 'assisted' -> 'goals') <> 'array' then false
           else jsonb_array_length(body -> 'assisted' -> 'goals') <= 40
         end
     and coalesce(jsonb_typeof(body -> 'map'), 'null') in ('string', 'null')
     and char_length(coalesce(body ->> 'map', '')) <= 40
$$;

-- Error si con ese JSON las BO de la cuenta pasan los 20 MB (sin contar lo que ocupa hoy esa misma BO).
create or replace function public.check_build_order_space(p_owner uuid, p_id uuid, p_body jsonb) returns void
language plpgsql security definer set search_path = '' as $$
begin
  if (select coalesce(sum(pg_column_size(body)), 0) from public.build_orders where owner_id = p_owner and id <> p_id)
     + pg_column_size(p_body) > 20 * 1024 * 1024 then
    raise exception 'Tus build orders ocupan más de 20 MB: borrá alguna antes de guardar otra.' using errcode = '23514';
  end if;
end $$;

-- BD-13: los topes de cada cuenta se cuentan de a uno por vez: sin el candado, pedidos simultáneos los pasaban.
create or replace function public.limit_new_build_orders() returns trigger
language plpgsql security definer set search_path = '' as $$
begin
  perform pg_advisory_xact_lock(hashtextextended('build_orders:' || new.owner_id::text, 0));
  if (select count(*) from public.build_orders
       where owner_id = new.owner_id and created_at > now() - interval '24 hours') >= 150 then
    raise exception 'Publicaste muchas build orders en las últimas 24 horas: probá de nuevo mañana.' using errcode = '23514';
  end if;
  perform public.check_build_order_space(new.owner_id, new.id, new.body);
  return new;
end $$;

-- Solo si cambia el JSON (votar, cambiar la visibilidad o reemplazar por lo mismo no cuentan).
create or replace function public.limit_build_order_changes() returns trigger
language plpgsql security definer set search_path = '' as $$
begin
  perform pg_advisory_xact_lock(hashtextextended('build_orders:' || new.owner_id::text, 0));
  if old.updated_at > now() - interval '5 seconds' then
    raise exception 'Guardaste esta build order hace unos segundos: esperá un momento y probá de nuevo.' using errcode = 'BC430';
  end if;
  perform public.check_build_order_space(new.owner_id, new.id, new.body);
  return new;
end $$;

drop trigger if exists build_orders_limit_changes on public.build_orders;
create trigger build_orders_limit_changes
  before update on public.build_orders
  for each row
  when (old.body is distinct from new.body)
  execute function public.limit_build_order_changes();

revoke execute on function public.check_build_order_space(uuid, uuid, jsonb) from public, anon, authenticated;
revoke execute on function public.limit_build_order_changes() from public, anon, authenticated;

-- ---------------------------------------------------------------------------------------------------------------
-- Cuentas y votos.
-- ---------------------------------------------------------------------------------------------------------------

-- BD-06: con la clave pública, GET /rest/v1/profiles listaba a todos los registrados aunque nunca hubieran publicado y
-- GET /rest/v1/votes decía quién votó qué, y la página de privacidad promete otra cosa. Un perfil se ve si es el propio
-- o si tiene una BO pública (el autor de esa BO), y cada uno ve solo sus votos. El contador sigue andando: lo mantiene
-- count_votes, que corre con los permisos de su dueño.
drop policy if exists "Los perfiles son públicos" on public.profiles;
drop policy if exists "Tu perfil y los de quien publica" on public.profiles;
create policy "Tu perfil y los de quien publica" on public.profiles for select
  using (
    id = auth.uid()
    or exists (select 1 from public.build_orders b where b.owner_id = profiles.id and b.visibility = 'public'));

drop policy if exists "Los votos de las BO que se ven" on public.votes;
drop policy if exists "Tus votos" on public.votes;
create policy "Tus votos" on public.votes for select to authenticated using (user_id = auth.uid());

-- BD-13: dos votos a la vez dejaban el contador uno abajo: cada uno contaba sin ver el voto del otro, todavía sin
-- confirmar. Con la fila tomada antes de contar, el segundo espera y cuenta con el primero ya confirmado. FOR NO KEY UPDATE
-- y no FOR UPDATE: la clave foránea de cada voto ya tiene FOR KEY SHARE sobre la BO, y con FOR UPDATE dos votos a la vez
-- se esperarían entre sí hasta que la base aborte uno.
create or replace function public.count_votes() returns trigger
language plpgsql security definer set search_path = '' as $$
declare
  target uuid := coalesce(new.build_order_id, old.build_order_id);
begin
  perform 1 from public.build_orders where id = target for no key update;
  update public.build_orders
     set votes = (select count(*) from public.votes where build_order_id = target)
   where id = target;
  return null;
end $$;

-- BD-13: el tope de tokens, también de a uno por vez.
create or replace function public.create_app_token(p_label text default 'BO Coach') returns text
language plpgsql security definer set search_path = '' as $$
declare
  token text := 'bocoach_' || replace(gen_random_uuid()::text || gen_random_uuid()::text, '-', '');
begin
  if auth.uid() is null then
    raise exception 'Hace falta iniciar sesión.' using errcode = '42501';
  end if;
  perform pg_advisory_xact_lock(hashtextextended('app_tokens:' || auth.uid()::text, 0));
  if (select count(*) from public.app_tokens where user_id = auth.uid()) >= 20 then
    raise exception 'Ya tenés 20 tokens: borrá alguno que no uses antes de crear otro.' using errcode = '23514';
  end if;
  insert into public.app_tokens (user_id, token_hash, label)
  values (auth.uid(), public.hash_token(token), coalesce(nullif(trim(p_label), ''), 'BO Coach'));
  return token;
end $$;
