import type { NextConfig } from "next";
import { securityHeaders } from "./src/lib/securityHeaders";

const nextConfig: NextConfig = {
  poweredByHeader: false,
  // CSP y compañía para toda la web (ver src/lib/securityHeaders.ts).
  async headers() {
    return [
      {
        source: "/(.*)",
        headers: securityHeaders({
          supabaseUrl: process.env.NEXT_PUBLIC_SUPABASE_URL ?? "",
          dev: process.env.NODE_ENV !== "production",
        }),
      },
      {
        // Los archivos del Core en WebAssembly con huella en el nombre (System.Linq.ixt1rg3huc.wasm) no cambian
        // nunca: el navegador los guarda un año sin volver a preguntar. dotnet.js (que los nombra) y los datos del
        // juego no tienen huella y se revalidan en cada visita.
        source: "/coach/_framework/:file([^/]+\\.[a-z0-9]{10}\\.(?:wasm|js))",
        headers: [{ key: "Cache-Control", value: "public, max-age=31536000, immutable" }],
      },
    ];
  },
  cacheComponents: true,
  // D10: las páginas de cruces leen public/coach/matchup_stats.json; se arman al compilar, pero si alguna se arma en
  // el servidor (una dirección que no estaba), el archivo tiene que viajar con la función.
  outputFileTracingIncludes: {
    "/cruces/[slug]": ["./public/coach/matchup_stats.json"],
    "/cruces": ["./public/coach/matchup_stats.json"],
    "/sitemap.xml": ["./public/coach/matchup_stats.json"],
    "/bo/[code]": ["./public/coach/matchup_stats.json"],
  },
  partialPrefetching: true,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
