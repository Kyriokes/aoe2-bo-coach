import type { MetadataRoute } from "next";
import { siteUrl } from "@/lib/site";

/** La biblioteca y las BO se pueden indexar; la cuenta, el login y la API no. */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", allow: "/", disallow: ["/cuenta", "/login", "/auth/", "/api/"] },
    sitemap: `${siteUrl()}/sitemap.xml`,
  };
}
