import { CIV_NAMES } from "./civNames";

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

/** La civ en minúsculas, como la guarda la base, si es una del juego o "any"; undefined si es inventada. */
export function knownCiv(value: string | undefined): string | undefined {
  const civ = filterValue(value)?.toLowerCase();
  return civ && (civ === "any" || Object.hasOwn(CIV_NAMES, civ)) ? civ : undefined;
}

/**
 * Las etiquetas que tienen página propia y van en el desplegable: las de las BO del repo (un test lo controla). Cada BO
 * escribe las que quiera y se puede filtrar por ellas desde su página, pero una inventada no se ofrece ni se indexa.
 */
const KNOWN_TAGS: ReadonlySet<string> = new Set([
  "arabia",
  "archers",
  "boom",
  "buildorderguide",
  "castillos",
  "castle",
  "fast-castle",
  "fast_castle",
  "feudal",
  "genérica",
  "importada",
  "maa",
  "mejorada",
  "scouts",
]);

/** La etiqueta en minúsculas, como la guarda la base, si es una de las conocidas; undefined si no. */
export function knownTag(value: string | undefined): string | undefined {
  const tag = filterValue(value)?.toLowerCase();
  return tag && KNOWN_TAGS.has(tag) ? tag : undefined;
}

/**
 * Qué página es la biblioteca con esos filtros para los buscadores: la civ o la etiqueta si tienen página propia (la
 * civ gana), y noindex para las búsquedas, para civ con etiqueta y para lo inventado ("?civ=casino online gratis" no
 * tiene que armar una página indexable con ese título).
 */
export function libraryPageFor(query: { q?: string; civ?: string; tag?: string }): { civ?: string; tag?: string; noindex: boolean } {
  const civ = knownCiv(query.civ);
  const tag = knownTag(query.tag);
  const invented = (query.civ !== undefined && civ === undefined) || (query.tag !== undefined && tag === undefined);
  return {
    ...(civ ? { civ } : tag ? { tag } : {}),
    noindex: Boolean(query.q) || (civ !== undefined && query.tag !== undefined) || invented,
  };
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
