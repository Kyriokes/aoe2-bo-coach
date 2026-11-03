-- Registros de partida (docs/superpowers/specs/2026-11-02-registro-de-partida-design.md).
-- Cada instalación de la app sube el registro de sus partidas con un secreto propio, sin cuenta: la app genera id y
-- secreto, y la primera reserva guarda el hash. Los archivos van a Storage (bucket privado "registros"), no a una tabla:
-- la base gratis tiene 500 MB. Acá solo queda qué se reservó, para poder validar, no duplicar y limitar. Con la clave
-- pública no se puede leer nada de esto: solo reservar, marcar subido y pedir el borrado de lo propio.

create table public.record_installs (
  id text primary key check (id ~ '^[0-9a-f]{32}$'),
  secret_hash text not null,
  created_at timestamptz not null default now(),
  last_seen_at timestamptz not null default now()
);

create table public.game_records (
  install_id text not null references public.record_installs (id) on delete cascade,
  game_id text not null check (game_id ~ '^[0-9a-f]{32}$'),
  path text not null unique,
  size int not null check (size between 1 and 1048576),
  app_version text not null check (char_length(app_version) between 1 and 40),
  reserved_at timestamptz not null default now(),
  uploaded_at timestamptz,
  forgotten_at timestamptz,
  primary key (install_id, game_id)
);

create index game_records_reserved_at on public.game_records (reserved_at);

alter table public.record_installs enable row level security;
alter table public.game_records enable row level security;
revoke all on table public.record_installs, public.game_records from public, anon, authenticated;

-- Los topes: por instalación y por día, en total por día, y lo guardado en total (el plan gratis de Storage es 1 GB).
create function public.record_limit(what text) returns bigint
language sql immutable as $$
  select case what
    when 'install_per_day' then 30
    when 'total_per_day' then 1000
    when 'total_bytes' then 900 * 1024 * 1024
  end::bigint
$$;

-- La instalación con ese secreto: la crea la primera vez y después exige el mismo. Error 28000 si no coincide.
create function public.record_install(p_install text, p_secret text) returns void
language plpgsql security definer set search_path = '' as $$
declare
  found_hash text;
begin
  if p_install is null or p_install !~ '^[0-9a-f]{32}$' or p_secret is null or p_secret !~ '^[0-9a-f]{64}$' then
    raise exception 'La identidad de la instalación no tiene el formato esperado.' using errcode = '23514';
  end if;
  insert into public.record_installs (id, secret_hash) values (p_install, public.hash_token(p_secret))
    on conflict (id) do nothing;
  select secret_hash into found_hash from public.record_installs where id = p_install;
  if found_hash <> public.hash_token(p_secret) then
    raise exception 'El secreto no es el de esa instalación.' using errcode = '28000';
  end if;
  update public.record_installs set last_seen_at = now() where id = p_install;
end $$;

-- Reserva el lugar de un registro. { path, status }: "new" = hay que subirlo, "exists" = ya estaba (reenviar no duplica).
create function public.app_record_reserve(p_install text, p_secret text, p_game text, p_size int, p_version text) returns jsonb
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
  if (select coalesce(sum(size), 0) from public.game_records where forgotten_at is null) + p_size > public.record_limit('total_bytes') then
    raise exception 'No hay lugar para más registros.' using errcode = 'BC429';
  end if;

  new_path := p_install || '/' || p_game || '.jsonl.gz';
  insert into public.game_records (install_id, game_id, path, size, app_version) values (p_install, p_game, new_path, p_size, p_version);
  return jsonb_build_object('path', new_path, 'status', 'new');
end $$;

-- El archivo ya está en Storage.
create function public.app_record_done(p_install text, p_secret text, p_game text) returns void
language plpgsql security definer set search_path = '' as $$
begin
  perform public.record_install(p_install, p_secret);
  update public.game_records set uploaded_at = now() where install_id = p_install and game_id = p_game and uploaded_at is null;
end $$;

-- El pedido de borrado: marca lo de la instalación y devuelve todas sus rutas marcadas para borrar los archivos. Si
-- algo falla a mitad de camino, otro pedido las vuelve a devolver. Las filas quedan (sin archivo) para no contar otra
-- vez la misma partida.
create function public.app_record_forget(p_install text, p_secret text) returns text[]
language plpgsql security definer set search_path = '' as $$
declare
  paths text[];
begin
  perform public.record_install(p_install, p_secret);
  update public.game_records set forgotten_at = now() where install_id = p_install and forgotten_at is null;
  select coalesce(array_agg(path order by path), '{}') into paths from public.game_records where install_id = p_install and forgotten_at is not null;
  return paths;
end $$;

-- Lo que las políticas de Storage preguntan (corren con los permisos de quien sube, que no ve las tablas).
create function public.record_is_reserved(p_path text) returns boolean
language sql stable security definer set search_path = '' as $$
  select exists (select 1 from public.game_records where path = p_path and uploaded_at is null and forgotten_at is null)
$$;

create function public.record_is_forgotten(p_path text) returns boolean
language sql stable security definer set search_path = '' as $$
  select exists (select 1 from public.game_records where path = p_path and forgotten_at is not null)
$$;

revoke execute on function public.record_install(text, text) from public, anon, authenticated;
revoke execute on function public.record_limit(text) from public, anon, authenticated;
revoke execute on function public.app_record_reserve(text, text, text, int, text) from public;
revoke execute on function public.app_record_done(text, text, text) from public;
revoke execute on function public.app_record_forget(text, text) from public;
revoke execute on function public.record_is_reserved(text) from public;
revoke execute on function public.record_is_forgotten(text) from public;
grant execute on function public.app_record_reserve(text, text, text, int, text) to anon, authenticated;
grant execute on function public.app_record_done(text, text, text) to anon, authenticated;
grant execute on function public.app_record_forget(text, text) to anon, authenticated;
grant execute on function public.record_is_reserved(text) to anon, authenticated;
grant execute on function public.record_is_forgotten(text) to anon, authenticated;

-- El bucket y sus políticas. Solo donde hay Storage (los tests sobre PGlite lo agregan a mano si lo quieren probar).
do $do$
begin
  if exists (select 1 from pg_namespace where nspname = 'storage') then
    insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
      values ('registros', 'registros', false, 1048576, array['application/gzip'])
      on conflict (id) do nothing;
    create policy "Registros: subir lo reservado" on storage.objects for insert to anon, authenticated
      with check (bucket_id = 'registros' and public.record_is_reserved(name));
    -- Storage pide poder ver un archivo para borrarlo: solo los del pedido de borrado.
    create policy "Registros: ver lo que se pidió borrar" on storage.objects for select to anon, authenticated
      using (bucket_id = 'registros' and public.record_is_forgotten(name));
    create policy "Registros: borrar lo que se pidió borrar" on storage.objects for delete to anon, authenticated
      using (bucket_id = 'registros' and public.record_is_forgotten(name));
  end if;
end $do$;
