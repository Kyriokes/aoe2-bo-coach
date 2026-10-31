-- BO privadas, suscripciones y lo que la app pide con su token (parte 3 de la app para el público, 2026-10-31).
--
-- Cada BO es pública o privada (lo ya publicado queda pública). Una privada la ve solo su dueño: no aparece en la
-- biblioteca, en su página, en el sitemap ni en la API, y no se puede votar ni suscribir. Cada uno se suscribe a BO
-- públicas de otros para tenerlas en la app. El mapa y el estilo de cada BO los calcula la base desde el JSON, para
-- que la app elija sin bajar la BO entera.
--
-- Se corre una vez en el SQL Editor de Supabase. Hasta entonces la web publicada sigue como antes.

-- ---------------------------------------------------------------------------------------------------------------
-- Estilo de una BO, copia de BuildOrderStyle del Core (un test compara las dos con todas las BO del repo): la
-- última unidad militar que entrena o, si no entrena ninguna y clica Castillos, fast_castle.
-- ---------------------------------------------------------------------------------------------------------------
create function public.build_order_style(body jsonb) returns text
language sql immutable set search_path = '' as $$
  with military as (
    select step ->> 'what' as unit
      from jsonb_array_elements(case when jsonb_typeof(body -> 'steps') = 'array' then body -> 'steps' else '[]'::jsonb end)
           with ordinality as s(step, position)
     where step ->> 'do' = 'train' and step ->> 'what' is not null and step ->> 'what' <> 'villager'
     order by position desc
     limit 1)
  select case
           when unit in ('archer', 'skirmisher') then 'archers'
           when unit in ('scout_cavalry', 'eagle_scout') then 'scouts'
           when unit in ('militia', 'spearman') then 'maa'
           when unit is null and exists (
             select 1 from jsonb_array_elements(case when jsonb_typeof(body -> 'steps') = 'array' then body -> 'steps' else '[]'::jsonb end) as t(step)
              where step ->> 'do' = 'research' and step ->> 'what' = 'castle_age') then 'fast_castle'
         end
    from (select (select unit from military) as unit) as found
$$;

alter table public.build_orders
  add column visibility text not null default 'public' check (visibility in ('public', 'private')),
  add column map text generated always as (body ->> 'map') stored,
  add column style text generated always as (public.build_order_style(body)) stored;

create index build_orders_visibility on public.build_orders (visibility);

grant insert (visibility) on public.build_orders to authenticated;
grant update (visibility) on public.build_orders to authenticated;

drop policy "Las BO son públicas" on public.build_orders;
create policy "Las públicas y las tuyas" on public.build_orders for select
  using (visibility = 'public' or owner_id = auth.uid());

-- Votos: solo a las BO que se ven (la subconsulta pasa por la política de lectura de build_orders).
drop policy "Los votos son públicos" on public.votes;
create policy "Los votos de las BO que se ven" on public.votes for select
  using (exists (select 1 from public.build_orders b where b.id = build_order_id));
drop policy "Votar con tu cuenta" on public.votes;
create policy "Votar con tu cuenta" on public.votes for insert to authenticated
  with check (user_id = auth.uid() and exists (select 1 from public.build_orders b where b.id = build_order_id));

-- ---------------------------------------------------------------------------------------------------------------
-- Suscripciones: las BO públicas de otros que cada uno quiere en su app. Las propias no hacen falta: siempre están.
-- ---------------------------------------------------------------------------------------------------------------
create table public.subscriptions (
  user_id uuid not null default auth.uid() references public.profiles (id) on delete cascade,
  build_order_id uuid not null references public.build_orders (id) on delete cascade,
  created_at timestamptz not null default now(),
  primary key (user_id, build_order_id)
);

create index subscriptions_build_order on public.subscriptions (build_order_id);

alter table public.subscriptions enable row level security;

create policy "Ver tus suscripciones" on public.subscriptions for select to authenticated using (user_id = auth.uid());
create policy "Sacar tus suscripciones" on public.subscriptions for delete to authenticated using (user_id = auth.uid());
create policy "Suscribirte a públicas ajenas" on public.subscriptions for insert to authenticated
  with check (
    user_id = auth.uid()
    and exists (select 1 from public.build_orders b
                 where b.id = build_order_id and b.visibility = 'public' and b.owner_id <> auth.uid()));

revoke all on public.subscriptions from anon, authenticated;
grant select, delete on public.subscriptions to authenticated;
grant insert (build_order_id) on public.subscriptions to authenticated;

-- ---------------------------------------------------------------------------------------------------------------
-- Lo que la app pide con su token (como app_publish: la web no necesita la clave de administrador).
-- ---------------------------------------------------------------------------------------------------------------

-- Tu cuenta y tus BO (de cualquier visibilidad) y tus suscriptas que siguen públicas, sin los pasos: para elegir
-- sin bajarlas. { "account": nombre, "items": [...] }.
create function public.app_my_build_orders(p_token text) returns jsonb
language plpgsql security definer set search_path = '' as $$
declare
  owner uuid := public.app_token_owner(p_token);
begin
  return jsonb_build_object(
    'account', (select display_name from public.profiles where id = owner),
    'items', coalesce((
    select jsonb_agg(jsonb_build_object(
             'code', b.code,
             'name', b.name,
             'civ', b.civ,
             'map', b.map,
             'style', b.style,
             'tags', to_jsonb(b.tags),
             -- Como GET /api/bo/<código>: author es la cuenta (lo que la app muestra al confirmar) y guide_author el
             -- autor que dice el JSON, que lo escribe cualquiera.
             'author', p.display_name,
             'guide_author', coalesce(nullif(trim(b.body ->> 'author'), ''), p.display_name),
             'own', b.owner_id = owner,
             'visibility', b.visibility,
             'updated_at', b.updated_at,
             'feudal_click', b.feudal_click,
             'castle_click', b.castle_click)
           order by b.owner_id = owner desc, b.name, b.code)
      from public.build_orders b
      join public.profiles p on p.id = b.owner_id
     where b.owner_id = owner
        or (b.visibility = 'public'
            and exists (select 1 from public.subscriptions s where s.user_id = owner and s.build_order_id = b.id))),
    '[]'::jsonb));
end $$;

-- Suscribirse a una BO por su código: 'own' si es tuya (no hace falta), 'subscribed' si es pública de otro.
create function public.app_subscribe(p_token text, p_code text) returns text
language plpgsql security definer set search_path = '' as $$
declare
  owner uuid := public.app_token_owner(p_token);
  target public.build_orders%rowtype;
begin
  select * into target from public.build_orders where code = lower(p_code);
  if found and target.owner_id = owner then
    return 'own';
  end if;
  if not found or target.visibility <> 'public' then
    raise exception 'No hay ninguna build order pública con el código %.', p_code using errcode = 'P0002';
  end if;
  insert into public.subscriptions (user_id, build_order_id) values (owner, target.id) on conflict do nothing;
  return 'subscribed';
end $$;

create function public.app_unsubscribe(p_token text, p_code text) returns void
language plpgsql security definer set search_path = '' as $$
declare
  owner uuid := public.app_token_owner(p_token);
begin
  delete from public.subscriptions s
   using public.build_orders b
   where s.build_order_id = b.id and s.user_id = owner and b.code = lower(p_code);
end $$;

-- Una BO entera (pública o tuya), con lo mismo que da GET /api/bo/<código>; null si no la podés ver.
create function public.app_build_order(p_token text, p_code text) returns jsonb
language plpgsql security definer set search_path = '' as $$
declare
  owner uuid := public.app_token_owner(p_token);
begin
  return (
    select jsonb_build_object(
             'code', b.code,
             'name', b.name,
             'civ', b.civ,
             'tags', to_jsonb(b.tags),
             'author', p.display_name,
             'guide_author', coalesce(nullif(trim(b.body ->> 'author'), ''), p.display_name),
             'votes', b.votes,
             'feudal_click', b.feudal_click,
             'castle_click', b.castle_click,
             'updated_at', b.updated_at,
             'visibility', b.visibility,
             'build_order', b.body)
      from public.build_orders b
      join public.profiles p on p.id = b.owner_id
     where b.code = lower(p_code) and (b.visibility = 'public' or b.owner_id = owner));
end $$;

revoke execute on function public.app_my_build_orders(text) from public;
revoke execute on function public.app_subscribe(text, text) from public;
revoke execute on function public.app_unsubscribe(text, text) from public;
revoke execute on function public.app_build_order(text, text) from public;
grant execute on function public.app_my_build_orders(text) to anon, authenticated;
grant execute on function public.app_subscribe(text, text) to anon, authenticated;
grant execute on function public.app_unsubscribe(text, text) to anon, authenticated;
grant execute on function public.app_build_order(text, text) to anon, authenticated;
