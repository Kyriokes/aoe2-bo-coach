import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { BoCard } from "@/components/BoCard";
import { listBuildOrders, listFacets, type LibraryQuery } from "@/lib/data";
import { filterValue, libraryHref, pageNumber } from "@/lib/filters";
import { mySubscriptionIds } from "@/lib/ownData";
import { getCurrentUser } from "@/lib/session";
import { civLabel, SITE_NAME } from "@/lib/share";
import { supabaseConfigured } from "@/lib/supabase/env";

const DESCRIPTION = "Buscá build orders de Age of Empires II, mirá su línea de tiempo simulada y abrilas en BO Coach.";

/** "de Britanos", "para cualquier civ" o null sin filtro de civ. */
function civSubject(civ: string | undefined) {
  if (!civ) return null;
  return civ === "any" ? "para cualquier civ" : `de ${civLabel(civ)}`;
}

/**
 * Cada civ y cada etiqueta es una página para los buscadores ("Build orders de Britanos"); las búsquedas por texto
 * y el orden no: se pueden seguir, pero no se indexan, y la canónica es la de la civ o la etiqueta.
 */
export async function generateMetadata({ searchParams }: PageProps<"/biblioteca">): Promise<Metadata> {
  const query = await readQuery(searchParams);
  const civ = filterValue(query.civ);
  const tag = civ ? undefined : filterValue(query.tag);
  const subject = civSubject(civ);
  const title = subject ? `Build orders ${subject}` : tag ? `Build orders: ${tag}` : "Biblioteca de build orders";
  const description = subject
    ? `Build orders de Age of Empires II ${subject}: cada paso, a qué minuto llegás a Feudal y Castillos, y para abrir en BO Coach.`
    : DESCRIPTION;
  const canonical = libraryHref(civ ? { civ } : tag ? { tag } : {}, query.page);
  return {
    title,
    description,
    alternates: { canonical },
    openGraph: { siteName: SITE_NAME, locale: "es_AR", type: "website", title, description, url: canonical },
    robots: query.q || (civ && query.tag) ? { index: false, follow: true } : undefined,
  };
}

type SearchParams = PageProps<"/biblioteca">["searchParams"];

function first(value: string | string[] | undefined) {
  return (Array.isArray(value) ? value[0] : value)?.trim() || undefined;
}

async function readQuery(searchParams: SearchParams): Promise<LibraryQuery> {
  const params = await searchParams;
  return {
    q: first(params.q),
    civ: first(params.civ),
    tag: first(params.tag),
    sort: first(params.orden) === "recientes" ? "recientes" : "votos",
    page: pageNumber(first(params.pagina)),
  };
}

export default function LibraryPage({ searchParams }: PageProps<"/biblioteca">) {
  return (
    <div className="flex flex-col gap-6">
      <section>
        <Suspense fallback={<h1 className="text-2xl font-semibold">Biblioteca de build orders</h1>}>
          <Title searchParams={searchParams} />
        </Suspense>
        <p className="mt-1 max-w-2xl text-sm text-muted">
          Buscá una BO, mirá su línea de tiempo y abrila en BO Coach para que el overlay te la vaya marcando durante la
          partida. ¿Tenés una propia? <Link href="/subir" className="text-amber hover:underline">Armala o subila</Link>.
        </p>
      </section>
      <Suspense fallback={<div className="h-10" />}>
        <Filters searchParams={searchParams} />
      </Suspense>
      <Suspense fallback={<p className="text-sm text-faint">Cargando la biblioteca…</p>}>
        <Results searchParams={searchParams} />
      </Suspense>
    </div>
  );
}

async function Title({ searchParams }: { searchParams: SearchParams }) {
  const subject = civSubject(filterValue((await readQuery(searchParams)).civ));
  return <h1 className="text-2xl font-semibold">{subject ? `Build orders ${subject}` : "Biblioteca de build orders"}</h1>;
}

async function Filters({ searchParams }: { searchParams: SearchParams }) {
  const [query, facets] = await Promise.all([readQuery(searchParams), listFacets()]);
  return (
    <form className="flex flex-wrap items-center gap-2" role="search">
      <input
        name="q"
        type="search"
        defaultValue={query.q}
        placeholder="Buscar por nombre o descripción"
        aria-label="Buscar build orders"
        className="field min-w-56 flex-1"
      />
      <select name="civ" defaultValue={query.civ ?? ""} className="field" aria-label="Civilización">
        <option value="">Todas las civs</option>
        {[...facets.civs].sort((a, b) => civLabel(a).localeCompare(civLabel(b), "es")).map((civ) => (
          <option key={civ} value={civ}>
            {civLabel(civ)}
          </option>
        ))}
      </select>
      <select name="tag" defaultValue={query.tag ?? ""} className="field" aria-label="Etiqueta">
        <option value="">Todas las etiquetas</option>
        {facets.tags.map((tag) => (
          <option key={tag} value={tag}>
            {tag}
          </option>
        ))}
      </select>
      <select name="orden" defaultValue={query.sort} className="field" aria-label="Orden">
        <option value="votos">Más votadas</option>
        <option value="recientes">Más nuevas</option>
      </select>
      <button type="submit" className="button-primary">
        Buscar
      </button>
    </form>
  );
}

async function Results({ searchParams }: { searchParams: SearchParams }) {
  const query = await readQuery(searchParams);
  if (!supabaseConfigured) {
    return <p className="text-sm text-faint">La web todavía no está conectada a la base de datos (falta configurar Supabase).</p>;
  }
  // La página sale del caché; la sesión y las suscripciones, no (solo para el botón "Usar en el coach").
  const [{ items, hasMore }, user, subscriptions] = await Promise.all([listBuildOrders(query), getCurrentUser(), mySubscriptionIds()]);
  const page = query.page ?? 1;
  if (items.length === 0) {
    return (
      <p className="text-sm text-faint">
        No hay build orders con esos filtros
        {page > 1 ? (
          <>
            {" "}
            en la página {page}. <Link href={libraryHref(query)} className="text-amber hover:underline">Ir a la primera</Link>.
          </>
        ) : (
          "."
        )}
      </p>
    );
  }
  return (
    <>
      <div className="grid gap-3 sm:grid-cols-2">
        {items.map((bo) => (
          <BoCard
            key={bo.code}
            bo={bo}
            subscription={user ? { subscribed: subscriptions?.has(bo.id) ?? false, own: bo.owner_id === user.id } : undefined}
          />
        ))}
      </div>
      {page > 1 || hasMore ? (
        <nav aria-label="Páginas de la biblioteca" className="flex items-center justify-between gap-3 text-sm">
          {page > 1 ? (
            <Link href={libraryHref(query, page - 1)} className="button" rel="prev">
              ← Anteriores
            </Link>
          ) : (
            <span />
          )}
          <span className="text-faint">Página {page}</span>
          {hasMore ? (
            <Link href={libraryHref(query, page + 1)} className="button" rel="next">
              Siguientes →
            </Link>
          ) : (
            <span />
          )}
        </nav>
      ) : null}
    </>
  );
}
