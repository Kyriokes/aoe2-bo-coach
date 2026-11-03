import type { MetadataRoute } from "next";
import { languageAlternates, LOCALES, localePath } from "@/i18n/locales";
import { listFacets, listSitemapEntries } from "@/lib/data";
import { filterValue } from "@/lib/filters";
import { getMatchups } from "@/lib/matchupsServer";
import { matchupSitemapPaths } from "@/lib/matchupView";
import { siteUrl } from "@/lib/site";

type Entry = MetadataRoute.Sitemap[number];

/**
 * La portada, la biblioteca, la página de cada civ que tiene BO, la página para subir, el roadmap, cada BO publicada
 * (sin Supabase configurado, solo las fijas) y las de /cruces (D10: cada civ y cada pareja). Cada una en todos los
 * idiomas, y cada URL dice cuáles son las otras (hreflang).
 */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const base = siteUrl();
  const absolute = (path: string) => `${base}${path === "/" ? "" : path}`;
  const inEveryLanguage = (path: string, entry: Omit<Entry, "url" | "alternates">): Entry[] => {
    const languages = Object.fromEntries(Object.entries(languageAlternates(path)).map(([locale, url]) => [locale, absolute(url)]));
    return LOCALES.map((locale) => ({ ...entry, url: absolute(localePath(locale, path)), alternates: { languages } }));
  };

  const [buildOrders, facets] = await Promise.all([listSitemapEntries(), listFacets()]);
  const matchups = getMatchups();
  return [
    ...inEveryLanguage("/", { changeFrequency: "monthly", priority: 1 }),
    ...inEveryLanguage("/biblioteca", { changeFrequency: "daily", priority: 0.8 }),
    ...facets.civs
      .filter((civ) => filterValue(civ) === civ)
      .flatMap((civ) => inEveryLanguage(`/biblioteca?civ=${encodeURIComponent(civ)}`, { changeFrequency: "weekly", priority: 0.6 })),
    ...inEveryLanguage("/roadmap", { changeFrequency: "monthly", priority: 0.6 }),
    ...inEveryLanguage("/subir", { changeFrequency: "monthly", priority: 0.3 }),
    ...inEveryLanguage("/privacidad", { changeFrequency: "yearly", priority: 0.1 }),
    ...(matchups ? matchupSitemapPaths(matchups).flatMap((path) => inEveryLanguage(path, { changeFrequency: "monthly", priority: 0.5 })) : []),
    ...buildOrders.flatMap((bo) => inEveryLanguage(`/bo/${bo.code}`, { lastModified: bo.updated_at, changeFrequency: "weekly", priority: 0.7 })),
  ];
}
