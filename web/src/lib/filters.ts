/**
 * Lo que puede valer un filtro de civ o de etiqueta: letras (con acentos), números, espacios y - _ . +, hasta 40
 * caracteres. Los valores van tal cual al filtro de PostgREST (eq.<civ>, cs.{<tag>}), donde una llave, una coma o
 * comillas rompen la sintaxis y la API respondía 500.
 */
const FILTER_VALUE = /^[\p{L}\p{N} _.+-]{1,40}$/u;

/** El valor recortado si sirve para filtrar; undefined si está vacío, es muy largo o trae caracteres que no van. */
export function filterValue(value: string | undefined): string | undefined {
  const trimmed = value?.trim() ?? "";
  return FILTER_VALUE.test(trimmed) ? trimmed : undefined;
}

/** Hasta qué página se puede pedir: más allá no hay nada y cada una ocuparía un lugar en el caché. */
export const MAX_PAGE = 1000;

/** La página de la biblioteca (?pagina=2): un entero de 1 a MAX_PAGE; cualquier otra cosa es la primera. */
export function pageNumber(value: string | undefined): number {
  const page = Number(value?.trim() || "1");
  return Number.isInteger(page) && page >= 1 && page <= MAX_PAGE ? page : 1;
}

/** La dirección de la biblioteca con esos filtros y esa página (la primera y el orden por votos no se escriben). */
export function libraryHref(query: { q?: string; civ?: string; tag?: string; sort?: string }, page = 1) {
  const params = new URLSearchParams();
  if (query.q) params.set("q", query.q);
  if (query.civ) params.set("civ", query.civ);
  if (query.tag) params.set("tag", query.tag);
  if (query.sort === "recientes") params.set("orden", "recientes");
  if (page > 1) params.set("pagina", String(page));
  const search = params.toString();
  return search ? `/biblioteca?${search}` : "/biblioteca";
}
