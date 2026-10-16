-- Biblioteca de build orders de AoE2 BO Coach.
--
-- Todos pueden ver las BO; publicar, editar, borrar y votar pide cuenta (Discord o Google) y cada uno solo toca lo
-- suyo (RLS). La app de escritorio publica con un token personal que se crea en la web: se guarda solo su hash y
-- las funciones app_* lo validan, así la web no necesita la clave de administrador de Supabase.

-- ---------------------------------------------------------------------------------------------------------------
-- Perfiles: nombre y avatar públicos, creados al registrarse.
-- ---------------------------------------------------------------------------------------------------------------
create table public.profiles (
  id uuid primary key references auth.users (id) on delete cascade,
  display_name text not null default 'Jugador' check (char_length(display_name) between 1 and 60),
  avatar_url text,
  created_at timestamptz not null default now()
);

create function public.handle_new_user() returns trigger
language plpgsql security definer set search_path = '' as $$
begin
  insert into public.profiles (id, display_name, avatar_url)
  values (
    new.id,
    left(coalesce(
      new.raw_user_meta_data ->> 'full_name',
      new.raw_user_meta_data ->> 'name',
      new.raw_user_meta_data ->> 'user_name',
      'Jugador'), 60),
    new.raw_user_meta_data ->> 'avatar_url');
  return new;
end $$;

create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- ---------------------------------------------------------------------------------------------------------------
-- Build orders.
-- ---------------------------------------------------------------------------------------------------------------

-- Código corto para pegar en la app ("Importar desde la web"): 6 caracteres sin los que se confunden (0/o, 1/l/i).
create function public.new_short_code() returns text
language plpgsql volatile set search_path = '' as $$
declare
  alphabet constant text := 'abcdefghjkmnpqrstuvwxyz23456789';
  result text := '';
begin
  for i in 1..6 loop
    result := result || substr(alphabet, 1 + floor(random() * length(alphabet))::int, 1);
  end loop;
  return result;
end $$;

create table public.build_orders (
  id uuid primary key default gen_random_uuid(),
  code text not null unique default public.new_short_code(),
  owner_id uuid not null default auth.uid() references public.profiles (id) on delete cascade,
  name text not null check (char_length(name) between 1 and 100),
  civ text not null default 'any' check (char_length(civ) between 1 and 40),
  tags text[] not null default '{}' check (cardinality(tags) <= 12),
  description text check (char_length(description) <= 4000),
  schema_version int not null check (schema_version >= 1),
  -- La BO tal cual (el mismo JSON que la app guarda y exporta).
  body jsonb not null check (jsonb_typeof(body) = 'object' and coalesce(jsonb_typeof(body -> 'steps'), '') = 'array'),
  -- Clics de edad estimados por el Planner al publicar, en segundos (solo para mostrar y ordenar).
  feudal_click int,
  castle_click int,
  votes int not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create index build_orders_tags on public.build_orders using gin (tags);
create index build_orders_popular on public.build_orders (votes desc, created_at desc);
create index build_orders_owner on public.build_orders (owner_id);

create function public.touch_updated_at() returns trigger
language plpgsql set search_path = '' as $$
begin
  new.updated_at := now();
  return new;
end $$;

create trigger build_orders_touch
  before update on public.build_orders
  for each row execute function public.touch_updated_at();

-- ---------------------------------------------------------------------------------------------------------------
-- Votos: uno por usuario y BO; el contador de build_orders lo mantiene un trigger.
-- ---------------------------------------------------------------------------------------------------------------
create table public.votes (
  user_id uuid not null default auth.uid() references public.profiles (id) on delete cascade,
  build_order_id uuid not null references public.build_orders (id) on delete cascade,
  created_at timestamptz not null default now(),
  primary key (user_id, build_order_id)
);

create function public.count_votes() returns trigger
language plpgsql security definer set search_path = '' as $$
declare
  target uuid := coalesce(new.build_order_id, old.build_order_id);
begin
  update public.build_orders
     set votes = (select count(*) from public.votes where build_order_id = target)
   where id = target;
  return null;
end $$;

create trigger votes_count
  after insert or delete on public.votes
  for each row execute function public.count_votes();

-- ---------------------------------------------------------------------------------------------------------------
-- Tokens de la app de escritorio.
-- ---------------------------------------------------------------------------------------------------------------
create table public.app_tokens (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null default auth.uid() references public.profiles (id) on delete cascade,
  token_hash text not null unique,
  label text not null default 'BO Coach' check (char_length(label) between 1 and 60),
  created_at timestamptz not null default now(),
  last_used_at timestamptz
);

create function public.hash_token(token text) returns text
language sql immutable set search_path = '' as $$
  select encode(sha256(convert_to(token, 'UTF8')), 'hex')
$$;

-- Crea un token para el usuario logueado y lo devuelve una sola vez (después solo queda el hash).
create function public.create_app_token(p_label text default 'BO Coach') returns text
language plpgsql security definer set search_path = '' as $$
declare
  token text := 'bocoach_' || replace(gen_random_uuid()::text || gen_random_uuid()::text, '-', '');
begin
  if auth.uid() is null then
    raise exception 'Hace falta iniciar sesión.' using errcode = '42501';
  end if;
  insert into public.app_tokens (user_id, token_hash, label)
  values (auth.uid(), public.hash_token(token), coalesce(nullif(trim(p_label), ''), 'BO Coach'));
  return token;
end $$;

-- Dueño de un token válido (y marca su último uso), o error si no existe.
create function public.app_token_owner(p_token text) returns uuid
language plpgsql security definer set search_path = '' as $$
declare
  owner uuid;
begin
  update public.app_tokens
     set last_used_at = now()
   where token_hash = public.hash_token(p_token)
  returning user_id into owner;
  if owner is null then
    raise exception 'El token de la app no es válido: creá uno nuevo en la web.' using errcode = '28000';
  end if;
  return owner;
end $$;

-- Publica (o actualiza, si se pasa el código de una BO propia) desde la app. Devuelve el código.
create function public.app_publish(
  p_token text,
  p_body jsonb,
  p_feudal_click int default null,
  p_castle_click int default null,
  p_code text default null
) returns text
language plpgsql security definer set search_path = '' as $$
declare
  owner uuid := public.app_token_owner(p_token);
  result text;
begin
  if p_code is null then
    insert into public.build_orders (owner_id, name, civ, tags, description, schema_version, body, feudal_click, castle_click)
    values (
      owner,
      p_body ->> 'name',
      coalesce(p_body ->> 'civ', 'any'),
      coalesce(array(select jsonb_array_elements_text(coalesce(p_body -> 'tags', '[]'::jsonb))), '{}'),
      p_body ->> 'description',
      (p_body ->> 'schema_version')::int,
      p_body,
      p_feudal_click,
      p_castle_click)
    returning code into result;
  else
    update public.build_orders
       set name = p_body ->> 'name',
           civ = coalesce(p_body ->> 'civ', 'any'),
           tags = coalesce(array(select jsonb_array_elements_text(coalesce(p_body -> 'tags', '[]'::jsonb))), '{}'),
           description = p_body ->> 'description',
           schema_version = (p_body ->> 'schema_version')::int,
           body = p_body,
           feudal_click = p_feudal_click,
           castle_click = p_castle_click
     where code = p_code and owner_id = owner
    returning code into result;
    if result is null then
      raise exception 'No hay una BO tuya con el código %.', p_code using errcode = '42501';
    end if;
  end if;
  return result;
end $$;

-- ---------------------------------------------------------------------------------------------------------------
-- Permisos.
-- ---------------------------------------------------------------------------------------------------------------
alter table public.profiles enable row level security;
alter table public.build_orders enable row level security;
alter table public.votes enable row level security;
alter table public.app_tokens enable row level security;

create policy "Los perfiles son públicos" on public.profiles for select using (true);
create policy "Cada uno edita su perfil" on public.profiles for update to authenticated
  using (id = auth.uid()) with check (id = auth.uid());

create policy "Las BO son públicas" on public.build_orders for select using (true);
create policy "Publicar con tu cuenta" on public.build_orders for insert to authenticated
  with check (owner_id = auth.uid());
create policy "Editar las tuyas" on public.build_orders for update to authenticated
  using (owner_id = auth.uid()) with check (owner_id = auth.uid());
create policy "Borrar las tuyas" on public.build_orders for delete to authenticated
  using (owner_id = auth.uid());

create policy "Los votos son públicos" on public.votes for select using (true);
create policy "Votar con tu cuenta" on public.votes for insert to authenticated with check (user_id = auth.uid());
create policy "Sacar tu voto" on public.votes for delete to authenticated using (user_id = auth.uid());

create policy "Ver tus tokens" on public.app_tokens for select to authenticated using (user_id = auth.uid());
create policy "Borrar tus tokens" on public.app_tokens for delete to authenticated using (user_id = auth.uid());

-- Columnas: el código, el dueño, los votos y las fechas no se tocan a mano.
revoke insert, update on public.build_orders from anon, authenticated;
grant insert (name, civ, tags, description, schema_version, body, feudal_click, castle_click)
  on public.build_orders to authenticated;
grant update (name, civ, tags, description, schema_version, body, feudal_click, castle_click)
  on public.build_orders to authenticated;
revoke insert, update on public.app_tokens from anon, authenticated;
revoke update on public.votes from anon, authenticated;

-- Las funciones internas no se llaman desde afuera; las app_* y create_app_token sí.
revoke execute on function public.app_token_owner(text) from public, anon, authenticated;
revoke execute on function public.create_app_token(text) from public, anon;
grant execute on function public.create_app_token(text) to authenticated;
grant execute on function public.app_publish(text, jsonb, int, int, text) to anon, authenticated;
