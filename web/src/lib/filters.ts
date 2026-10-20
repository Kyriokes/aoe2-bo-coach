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
