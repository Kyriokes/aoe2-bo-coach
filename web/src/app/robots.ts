import type { MetadataRoute } from "next";
import { siteUrl } from "@/lib/site";

/**
 * La API y la vuelta del login no se recorren. La cuenta y el login sí, para que los buscadores vean su noindex:
 * bloqueados acá, podían quedar indexados (sin contenido) por los enlaces que llegan a ellos.
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", allow: "/", disallow: ["/auth/", "/api/"] },
    sitemap: `${siteUrl()}/sitemap.xml`,
  };
}
