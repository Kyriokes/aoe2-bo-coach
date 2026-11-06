/** El repositorio de la app (código abierto, MIT). */
export const REPO_URL = "https://github.com/Kyriokes/aoe2-bo-coach";

/**
 * El .zip de la última versión de la app, directo: la página de releases de GitHub muestra varias descargas y quien no
 * la conoce no sabe cuál bajar. release.yml sube el mismo .zip también con este nombre sin versión, y el aviso de
 * versión nueva de la app enlaza lo mismo (UpdateCheck.DownloadUrl).
 */
export const DOWNLOAD_URL = `${REPO_URL}/releases/latest/download/BoCoach-win-x64.zip`;

/**
 * Dirección pública de la web, sin barra al final: la de NEXT_PUBLIC_SITE_URL si está, si no la de producción de
 * Vercel (la variable la pone Vercel al compilar) y, en desarrollo, localhost. Sirve para los enlaces absolutos de la
 * vista previa al compartir, el sitemap y robots.txt.
 */
export function siteUrl(env: Record<string, string | undefined> = process.env) {
  const configured = env.NEXT_PUBLIC_SITE_URL?.trim();
  const vercel = env.VERCEL_PROJECT_PRODUCTION_URL?.trim();
  const url = configured || (vercel ? `https://${vercel}` : "http://localhost:3000");
  return url.replace(/\/+$/, "");
}
