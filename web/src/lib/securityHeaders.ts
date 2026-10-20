/**
 * Cabeceras de seguridad de toda la web (las manda next.config.ts). La CSP deja solo scripts propios más el Core en
 * WebAssembly ('wasm-unsafe-eval'); Next mete scripts en línea para hidratar, así que hace falta 'unsafe-inline'
 * (sin nonce: con las páginas cacheadas no hay uno por pedido). Nada puede embeber la web en un iframe.
 */
export interface HeaderOptions {
  /** NEXT_PUBLIC_SUPABASE_URL: el navegador habla con ese proyecto (login y datos, también por socket). */
  supabaseUrl: string;
  /** En desarrollo, el recargado en caliente de Next necesita eval. */
  dev?: boolean;
}

export function contentSecurityPolicy({ supabaseUrl, dev = false }: HeaderOptions): string {
  const supabase = supabaseUrl.trim().replace(/\/+$/, "");
  const connect = ["'self'"];
  if (supabase) connect.push(supabase, supabase.replace(/^https:/, "wss:"));
  const script = ["'self'", "'unsafe-inline'", "'wasm-unsafe-eval'"];
  if (dev) script.push("'unsafe-eval'");

  return [
    "default-src 'self'",
    `script-src ${script.join(" ")}`,
    "style-src 'self' 'unsafe-inline'",
    // Avatares de Discord y Google, y las imágenes de la vista previa al compartir.
    "img-src 'self' data: blob: https:",
    "font-src 'self' data:",
    `connect-src ${connect.join(" ")}`,
    "worker-src 'self' blob:",
    "frame-ancestors 'none'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
  ].join("; ");
}

export function securityHeaders(options: HeaderOptions): { key: string; value: string }[] {
  return [
    { key: "Content-Security-Policy", value: contentSecurityPolicy(options) },
    { key: "X-Content-Type-Options", value: "nosniff" },
    { key: "X-Frame-Options", value: "DENY" },
    { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
    { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" },
  ];
}
