import type { NextRequest } from "next/server";
import { listBuildOrders } from "@/lib/data";
import { filterValue, pageNumber } from "@/lib/filters";
import { error, publishFromApp } from "./appApi";

/** GET /api/bo?q=&civ=&tag=&orden=votos|recientes&pagina=N: una página de la biblioteca (sin el JSON de cada BO). */
export async function GET(request: NextRequest) {
  const params = request.nextUrl.searchParams;
  // civ y tag van al filtro de la base: con caracteres que no van, 400 en vez de un error de PostgREST (500).
  const filters: Partial<Record<"civ" | "tag", string>> = {};
  for (const name of ["civ", "tag"] as const) {
    const raw = params.get(name);
    if (raw === null) continue;
    const value = filterValue(raw);
    if (value === undefined) return error(400, `El filtro ${name} solo admite letras, números, espacios y - _ . + (hasta 40 caracteres).`);
    filters[name] = value;
  }
  const { items, hasMore } = await listBuildOrders({
    q: params.get("q") ?? undefined,
    ...filters,
    sort: params.get("orden") === "recientes" ? "recientes" : "votos",
    page: pageNumber(params.get("pagina") ?? undefined),
  });
  return Response.json({ items, has_more: hasMore });
}

/** POST /api/bo: la app publica una BO nueva. Cuerpo: { build_order, feudal_click?, castle_click? }. */
export async function POST(request: NextRequest) {
  return publishFromApp(request, null);
}
