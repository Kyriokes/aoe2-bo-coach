/** Un pedazo de texto; con href, es una dirección para mostrar como enlace. */
export type TextPart = { text: string; href?: string };

const URL_PATTERN = /https?:\/\/[^\s<>"'`]+/g;
const TRAILING = /[.,;:!?)\]}]+$/;

/**
 * Parte un texto en pedazos y marca las direcciones http(s), para que la descripción de una BO muestre como enlace la
 * guía original (las importadas terminan en "Fuente: https://..."). Solo http y https: la descripción la escribe
 * cualquiera. La puntuación del final (el punto de la frase, el paréntesis que cierra) queda afuera del enlace.
 */
export function linkify(text: string): TextPart[] {
  const parts: TextPart[] = [];
  let last = 0;
  for (const match of text.matchAll(URL_PATTERN)) {
    const url = match[0].replace(TRAILING, "");
    const start = match.index;
    if (!URL.canParse(url) || new URL(url).hostname === "") continue;
    if (start > last) parts.push({ text: text.slice(last, start) });
    parts.push({ text: url, href: url });
    last = start + url.length;
  }
  if (last < text.length) parts.push({ text: text.slice(last) });
  return parts;
}
