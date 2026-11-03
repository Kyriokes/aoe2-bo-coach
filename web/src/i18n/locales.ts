/**
 * Los idiomas de la web. Para sumar uno: el código acá (y en las tablas de abajo), su diccionario (i18n/xx.ts), la
 * columna en lib/civNames.ts, el idioma en StepText del Core y en lib/stepText.ts, y content/roadmap.xx.json. Las
 * rutas, el sitemap, los hreflang y el selector salen de esta lista.
 */
export const LOCALES = ["es", "en"] as const;
export type Locale = (typeof LOCALES)[number];

/** El idioma de las URL sin prefijo (las que ya estaban indexadas). */
export const DEFAULT_LOCALE: Locale = "es";

/** La cookie con el idioma que eligió el visitante en el selector. */
export const LOCALE_COOKIE = "idioma";

/** El nombre de cada idioma en su propio idioma, para el selector. */
export const LOCALE_NAME: Readonly<Record<Locale, string>> = { es: "Español", en: "English" };

/** Para fechas, números y orden alfabético. */
export const LANG_TAG: Readonly<Record<Locale, string>> = { es: "es-AR", en: "en-US" };

/** Para og:locale. */
export const OG_LOCALE: Readonly<Record<Locale, string>> = { es: "es_AR", en: "en_US" };

export function isLocale(value: unknown): value is Locale {
  return typeof value === "string" && (LOCALES as readonly string[]).includes(value);
}

/** El idioma pedido si es uno de los nuestros; si no, el de por defecto (lo que llega del navegador no es confiable). */
export function asLocale(value: unknown): Locale {
  return isLocale(value) ? value : DEFAULT_LOCALE;
}

/** "/biblioteca?civ=x" en ese idioma: igual en castellano, "/en/biblioteca?civ=x" en inglés. */
export function localePath(locale: Locale, path: string): string {
  if (locale === DEFAULT_LOCALE) return path;
  const end = path.search(/[?#]/);
  const pathname = end < 0 ? path : path.slice(0, end);
  const rest = end < 0 ? "" : path.slice(end);
  return `/${locale}${pathname === "/" ? "" : pathname}${rest}`;
}

/** El idioma de una ruta y la ruta sin él. `prefixed` dice si la ruta traía el idioma escrito. */
export function stripLocale(path: string): { locale: Locale; path: string; prefixed: boolean } {
  const match = /^\/([a-z]{2})(?=$|[/?#])(.*)$/.exec(path);
  if (!match || !isLocale(match[1])) return { locale: DEFAULT_LOCALE, path, prefixed: false };
  const rest = match[2];
  return { locale: match[1], path: rest.startsWith("/") ? rest : `/${rest}`, prefixed: true };
}

/** Las URL de una misma página en cada idioma, para los hreflang y el sitemap. */
export function languageAlternates(path: string): Record<string, string> {
  return Object.fromEntries(LOCALES.map((locale) => [locale, localePath(locale, path)]));
}

/** Canónica y hreflang de una página (`x-default` es el idioma de por defecto). */
export function localeAlternates(locale: Locale, path: string) {
  return {
    canonical: localePath(locale, path),
    languages: { ...languageAlternates(path), "x-default": localePath(DEFAULT_LOCALE, path) },
  };
}
