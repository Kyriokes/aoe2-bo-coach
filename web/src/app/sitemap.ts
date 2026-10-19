import type { MetadataRoute } from "next";
import { listSitemapEntries } from "@/lib/data";
import { siteUrl } from "@/lib/site";

/** La portada, la biblioteca, la página para subir y cada BO publicada (sin Supabase configurado, solo las tres primeras). */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const base = siteUrl();
  const buildOrders = await listSitemapEntries();
  return [
    { url: base, changeFrequency: "monthly", priority: 1 },
    { url: `${base}/biblioteca`, changeFrequency: "daily", priority: 0.8 },
    { url: `${base}/subir`, changeFrequency: "monthly", priority: 0.3 },
    ...buildOrders.map((bo) => ({
      url: `${base}/bo/${bo.code}`,
      lastModified: bo.updated_at,
      changeFrequency: "weekly" as const,
      priority: 0.7,
    })),
  ];
}
