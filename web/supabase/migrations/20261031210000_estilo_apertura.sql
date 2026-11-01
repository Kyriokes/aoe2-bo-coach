-- El estilo de una BO es su apertura (2026-10-31).
--
-- build_order_style tomaba la última unidad militar que entrena la BO. Desde que "Scouts → CA (mejorada)" de los
-- hunos sigue en Castillos con arqueros a caballo, eso daba sin estilo (y lo mismo pasaba con las de caballeros o
-- camellos después de Castillos). Ahora es la última unidad militar antes del clic en Castillos, o la última que
-- entrena si antes no hay ninguna, como BuildOrderStyle del Core (un test compara las dos con todas las BO del repo).
-- La columna style es generada y guardada: se recalcula en las filas donde cambia.
--
-- Se corre una vez en el SQL Editor de Supabase.

create or replace function public.build_order_style(body jsonb) returns text
language sql immutable set search_path = '' as $$
  with steps as (
    select step, position
      from jsonb_array_elements(case when jsonb_typeof(body -> 'steps') = 'array' then body -> 'steps' else '[]'::jsonb end)
           with ordinality as s(step, position)),
  castle as (
    select min(position) as position from steps where step ->> 'do' = 'research' and step ->> 'what' = 'castle_age'),
  military as (
    select step ->> 'what' as unit, position
      from steps
     where step ->> 'do' = 'train' and step ->> 'what' is not null and step ->> 'what' <> 'villager'),
  opening as (
    select coalesce(
             (select unit from military where position < coalesce((select position from castle), 'infinity'::numeric)
               order by position desc limit 1),
             (select unit from military order by position desc limit 1)) as unit)
  select case
           when unit in ('archer', 'skirmisher') then 'archers'
           when unit in ('scout_cavalry', 'eagle_scout') then 'scouts'
           when unit in ('militia', 'spearman') then 'maa'
           when unit is null and (select position from castle) is not null then 'fast_castle'
         end
    from opening
$$;

update public.build_orders set body = body where style is distinct from public.build_order_style(body);
