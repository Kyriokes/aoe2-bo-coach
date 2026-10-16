import type { MetadataRoute } from "next";
import { listSitemapEntries } from "@/lib/data";
import { siteUrl } from "@/lib/site";

/** La biblioteca, la página para subir y cada BO publicada (sin Supabase configurado, solo las dos primeras). */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const base = siteUrl();
  const buildOrders = await listSitemapEntries();
  return [
    { url: base, changeFrequency: "daily", priority: 1 },
    { url: `${base}/subir`, changeFrequency: "monthly", priority: 0.3 },
    ...buildOrders.map((bo) => ({
      url: `${base}/bo/${bo.code}`,
      lastModified: bo.updated_at,
      changeFrequency: "weekly" as const,
      priority: 0.7,
    })),
  ];
}
