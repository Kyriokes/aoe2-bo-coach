import type { MetadataRoute } from "next";
import { listFacets, listSitemapEntries } from "@/lib/data";
import { filterValue } from "@/lib/filters";
import { getMatchups } from "@/lib/matchupsServer";
import { matchupSitemapPaths } from "@/lib/matchupView";
import { siteUrl } from "@/lib/site";

/**
 * La portada, la biblioteca, la página de cada civ que tiene BO, la página para subir, cada BO publicada (sin
 * Supabase configurado, solo las fijas) y las de /cruces (D10: cada civ y cada pareja).
 */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const base = siteUrl();
  const [buildOrders, facets] = await Promise.all([listSitemapEntries(), listFacets()]);
  return [
    { url: base, changeFrequency: "monthly", priority: 1 },
    { url: `${base}/biblioteca`, changeFrequency: "daily", priority: 0.8 },
    ...facets.civs
      .filter((civ) => filterValue(civ) === civ)
      .map((civ) => ({
        url: `${base}/biblioteca?civ=${encodeURIComponent(civ)}`,
        changeFrequency: "weekly" as const,
        priority: 0.6,
      })),
    { url: `${base}/subir`, changeFrequency: "monthly", priority: 0.3 },
    { url: `${base}/privacidad`, changeFrequency: "yearly", priority: 0.1 },
    ...(() => {
      const matchups = getMatchups();
      return matchups
        ? matchupSitemapPaths(matchups).map((path) => ({ url: `${base}${path}`, changeFrequency: "monthly" as const, priority: 0.5 }))
        : [];
    })(),
    ...buildOrders.map((bo) => ({
      url: `${base}/bo/${bo.code}`,
      lastModified: bo.updated_at,
      changeFrequency: "weekly" as const,
      priority: 0.7,
    })),
  ];
}
