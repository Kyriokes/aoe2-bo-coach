import { NextRequest } from "next/server";
import { describe, expect, it } from "vitest";
import { GET } from "./route";

const SITE = "https://sitio.example";

/** Adónde manda el selector con ese idioma y ese ?volver= (tal cual va en la dirección, sin codificar de nuevo). */
async function target(locale: string, volver: string) {
  const request = new NextRequest(`${SITE}/idioma/${locale}?volver=${volver}`);
  const response = await GET(request, { params: Promise.resolve({ locale }) });
  return response.headers.get("location");
}

describe("el selector de idioma", () => {
  it("vuelve a la misma página en el idioma elegido", async () => {
    expect(await target("en", "/biblioteca?civ=franks")).toBe(`${SITE}/en/biblioteca?civ=franks`);
    expect(await target("es", "/en/cuenta")).toBe(`${SITE}/cuenta`);
    expect(await target("en", "/en/cuenta")).toBe(`${SITE}/en/cuenta`);
    expect(await target("es", "/")).toBe(`${SITE}/`);
  });

  it("guarda la elección en la cookie", async () => {
    const response = await GET(new NextRequest(`${SITE}/idioma/en`), { params: Promise.resolve({ locale: "en" }) });
    expect(response.status).toBe(307);
    expect(response.cookies.get("idioma")?.value).toBe("en");
  });

  // Cada una pasaba el control de "ruta interna" y se volvía otro sitio al armar la dirección: el idioma se saca después
  // ("/en//x" queda "//x"), y el navegador lee "\" como "/" y borra los tabs.
  it.each([
    ["el idioma tapando las dos barras", "/en//evil.example"],
    ["una barra invertida codificada", "/en/%5Cevil.example"],
    ["un tab codificado", "/%09/evil.example"],
    ["dos barras", "//evil.example"],
    ["barra invertida directa", "/%5Cevil.example"],
    ["un salto de línea codificado", "/%0A/evil.example"],
    ["una dirección rota", "/en//["],
  ])("con %s se queda en el sitio", async (_name, volver) => {
    for (const locale of ["es", "en"]) {
      const location = await target(locale, volver);
      expect(new URL(location ?? "").origin, `${locale} ${volver}`).toBe(SITE);
    }
  });

  it("lo que apunta a otro sitio va a la portada del idioma", async () => {
    expect(await target("es", "/en//evil.example")).toBe(`${SITE}/`);
    expect(await target("en", "/%09/evil.example")).toBe(`${SITE}/en`);
  });

  it("un idioma que no es uno de los nuestros no rompe nada", async () => {
    expect(await target("xx", "/roadmap")).toBe(`${SITE}/roadmap`);
  });
});
