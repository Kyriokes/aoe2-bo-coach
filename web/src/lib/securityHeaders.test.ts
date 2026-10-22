import { describe, expect, it } from "vitest";
import { contentSecurityPolicy, securityHeaders } from "./securityHeaders";

const supabaseUrl = "https://abc.supabase.co";

function directive(csp: string, name: string) {
  return csp
    .split(";")
    .map((d) => d.trim())
    .find((d) => d.startsWith(`${name} `) || d === name);
}

describe("cabeceras de seguridad", () => {
  it("la CSP no deja embeber la web ni cargar objetos, y solo scripts propios (más el Core en WebAssembly)", () => {
    const csp = contentSecurityPolicy({ supabaseUrl });

    expect(directive(csp, "frame-ancestors")).toBe("frame-ancestors 'none'");
    expect(directive(csp, "object-src")).toBe("object-src 'none'");
    expect(directive(csp, "base-uri")).toBe("base-uri 'self'");
    expect(directive(csp, "default-src")).toBe("default-src 'self'");
    expect(directive(csp, "script-src")).toContain("'self'");
    expect(directive(csp, "script-src")).toContain("'wasm-unsafe-eval'");
    expect(directive(csp, "script-src")).not.toContain("'unsafe-eval'");
  });

  it("deja hablar con el proyecto de Supabase (auth y datos, incluido el socket) y mostrar avatares de afuera", () => {
    const csp = contentSecurityPolicy({ supabaseUrl });

    expect(directive(csp, "connect-src")).toContain("https://abc.supabase.co");
    expect(directive(csp, "connect-src")).toContain("wss://abc.supabase.co");
    expect(directive(csp, "img-src")).toContain("https:");
  });

  it("sin proyecto configurado no mete 'undefined' en la CSP", () => {
    const csp = contentSecurityPolicy({ supabaseUrl: "" });

    expect(csp).not.toContain("undefined");
    expect(directive(csp, "connect-src")).toBe("connect-src 'self'");
  });

  it("en desarrollo permite eval (lo necesita el recargado de Next) y en producción no", () => {
    expect(directive(contentSecurityPolicy({ supabaseUrl, dev: true }), "script-src")).toContain("'unsafe-eval'");
    expect(directive(contentSecurityPolicy({ supabaseUrl, dev: false }), "script-src")).not.toContain("'unsafe-eval'");
  });

  it("manda también nosniff, X-Frame-Options, la política de referer y COOP", () => {
    const headers = Object.fromEntries(securityHeaders({ supabaseUrl }).map((h) => [h.key, h.value]));

    expect(headers["X-Content-Type-Options"]).toBe("nosniff");
    expect(headers["X-Frame-Options"]).toBe("DENY");
    expect(headers["Referrer-Policy"]).toBe("strict-origin-when-cross-origin");
    expect(headers["Cross-Origin-Opener-Policy"]).toBe("same-origin");
    expect(headers["Content-Security-Policy"]).toBe(contentSecurityPolicy({ supabaseUrl }));
  });
});
