import Link from "next/link";
import { Suspense } from "react";
import { BoCard } from "@/components/BoCard";
import { listBuildOrders, listFacets, type LibraryQuery } from "@/lib/data";
import { civLabel } from "@/lib/share";
import { supabaseConfigured } from "@/lib/supabase/env";

type SearchParams = PageProps<"/">["searchParams"];

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
  };
}

export default function LibraryPage({ searchParams }: PageProps<"/">) {
  return (
    <div className="flex flex-col gap-6">
      <section>
        <h1 className="text-2xl font-semibold">Biblioteca de build orders</h1>
        <p className="mt-1 max-w-2xl text-sm text-muted">
          Buscá una BO, mirá su línea de tiempo y abrila en BO Coach para que el overlay te la vaya marcando durante la
          partida. ¿Tenés una propia? <Link href="/subir" className="text-amber hover:underline">Subila</Link>.
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
        {facets.civs.map((civ) => (
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
  const items = await listBuildOrders(query);
  if (items.length === 0) {
    return <p className="text-sm text-faint">No hay build orders con esos filtros.</p>;
  }
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {items.map((bo) => (
        <BoCard key={bo.code} bo={bo} />
      ))}
    </div>
  );
}
