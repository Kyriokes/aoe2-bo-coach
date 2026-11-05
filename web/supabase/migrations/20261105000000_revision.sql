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

-- Reserva el lugar de un registro. { path, status }: "new" = hay que subirlo, "exists" = ya estaba (reenviar no duplica).
create or replace function public.app_record_reserve(p_install text, p_secret text, p_game text, p_size int, p_version text) returns jsonb
language plpgsql security definer set search_path = '' as $$
declare
  existing public.game_records;
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
  if found then
    return jsonb_build_object('path', existing.path,
      'status', case when existing.uploaded_at is null and existing.forgotten_at is null then 'new' else 'exists' end);
  end if;

  if (select count(*) from public.game_records where install_id = p_install and reserved_at > now() - interval '1 day') >= public.record_limit('install_per_day') then
    raise exception 'Demasiadas partidas de esta instalación hoy.' using errcode = 'BC429';
  end if;
  if (select count(*) from public.game_records where reserved_at > now() - interval '1 day') >= public.record_limit('total_per_day') then
    raise exception 'Demasiadas partidas hoy.' using errcode = 'BC429';
  end if;
  -- Lo que hay en Storage, más lo reservado que todavía no se subió.
  if (select coalesce(sum((o.metadata ->> 'size')::bigint), 0) from storage.objects o where o.bucket_id = 'registros')
     + (select coalesce(sum(g.size), 0) from public.game_records g
         where g.uploaded_at is null and g.forgotten_at is null
           and not exists (select 1 from storage.objects o where o.bucket_id = 'registros' and o.name = g.path))
     + p_size > public.record_limit('total_bytes') then
    raise exception 'No hay lugar para más registros.' using errcode = 'BC429';
  end if;

  new_path := p_install || '/' || p_game || '.jsonl.gz';
  insert into public.game_records (install_id, game_id, path, size, app_version) values (p_install, p_game, new_path, p_size, p_version);
  return jsonb_build_object('path', new_path, 'status', 'new');
end $$;

-- El pedido de borrado: marca lo de la instalación y devuelve todas sus rutas marcadas para borrar los archivos. Si
-- algo falla a mitad de camino, otro pedido las vuelve a devolver. Las filas quedan (sin archivo) para no contar otra
-- vez la misma partida. Una instalación que nunca reservó no tiene nada que borrar.
create or replace function public.app_record_forget(p_install text, p_secret text) returns text[]
language plpgsql security definer set search_path = '' as $$
declare
  paths text[];
begin
  if not public.record_install_known(p_install, p_secret) then
    return '{}';
  end if;
  update public.game_records set forgotten_at = now() where install_id = p_install and forgotten_at is null;
  select coalesce(array_agg(path order by path), '{}') into paths from public.game_records where install_id = p_install and forgotten_at is not null;
  return paths;
end $$;

revoke execute on function public.record_install_known(text, text) from public, anon, authenticated;
