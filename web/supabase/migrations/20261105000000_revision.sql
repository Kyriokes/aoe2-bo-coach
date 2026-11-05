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

-- El archivo ya está en Storage.
create or replace function public.app_record_done(p_install text, p_secret text, p_game text) returns void
language plpgsql security definer set search_path = '' as $$
begin
  if not public.record_install_known(p_install, p_secret) then
    raise exception 'Esa instalación todavía no reservó nada.' using errcode = '28000';
  end if;
  update public.game_records set uploaded_at = now() where install_id = p_install and game_id = p_game and uploaded_at is null;
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
