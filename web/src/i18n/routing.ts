import { DEFAULT_LOCALE, isLocale, localePath, stripLocale, type Locale } from "./locales";

/**
 * Qué hace el proxy con una ruta (spec 2026-11-02-web-idiomas-roadmap, sección 1). El castellano no lleva prefijo en
 * el navegador (las URL indexadas de siempre) y por adentro va a /es/...; /es/... redirige a la ruta sin prefijo y
 * /en/... pasa. Quien eligió inglés en el selector (la cookie) va de una ruta sin prefijo a /en/... con 307: depende
 * de la cookie, así que nunca 308. Lo que no es una página de la web da 404 de verdad acá, porque una página
 * prerenderizada en partes ya no puede cambiar el estado.
 */
export type LocaleRoute =
  | { kind: "pass"; locale: Locale; path: string }
  | { kind: "rewrite"; to: string; locale: Locale; path: string }
  | { kind: "redirect"; to: string; status: 307 | 308 }
  | { kind: "notFound"; to: string };

/** El primer tramo de cada página de la web. */
const PAGES = new Set(["", "biblioteca", "bo", "subir", "cruces", "cuenta", "login", "privacidad", "roadmap", "opengraph-image", "twitter-image"]);

/** Lo que no pasa por el idioma: la API, la vuelta del login, el selector, el Core y lo interno de Next. */
const UNLOCALIZED = /^\/(?:_|api(?:\/|$)|auth(?:\/|$)|coach(?:\/|$)|idioma(?:\/|$))/;

/** Una imagen generada: Next escribe og:image con el segmento del idioma (/es/opengraph-image), y ahí se queda. */
const GENERATED_IMAGE = /\/(?:opengraph-image|twitter-image)(?:-[^/]*)?$/;

/** La ruta interna que dibuja el 404 con la cabecera y el pie del idioma ([...rest]/page.tsx). */
export function notFoundPath(locale: Locale) {
  return `/${locale}/404`;
}

export function localeRouting(pathname: string, cookie?: string): LocaleRoute {
  const lastSegment = pathname.slice(pathname.lastIndexOf("/") + 1);
  // Archivos de public/, sitemap.xml y robots.txt.
  if (UNLOCALIZED.test(pathname) || lastSegment.includes(".")) return { kind: "pass", locale: DEFAULT_LOCALE, path: pathname };

  const { locale, path, prefixed } = stripLocale(pathname);
  if (prefixed && locale === DEFAULT_LOCALE && !GENERATED_IMAGE.test(path)) return { kind: "redirect", to: path, status: 308 };
  if (!PAGES.has(path.split("/")[1] ?? "")) return { kind: "notFound", to: notFoundPath(locale) };
  if (prefixed) return { kind: "pass", locale, path };
  if (isLocale(cookie) && cookie !== DEFAULT_LOCALE && !GENERATED_IMAGE.test(path)) return { kind: "redirect", to: localePath(cookie, path), status: 307 };
  return { kind: "rewrite", to: `/${DEFAULT_LOCALE}${path === "/" ? "" : path}`, locale, path };
}
