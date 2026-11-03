import { describe, expect, it } from "vitest";
import { en } from "./en";
import { es } from "./es";
import { fill, getDictionary } from "./index";
import { asLocale, LOCALES, localeAlternates, localePath, stripLocale } from "./locales";
import { localeRouting } from "./routing";

/** Cada hoja de un diccionario con su camino ("home.steps.0.title"). */
function leaves(value: unknown, path = ""): Map<string, string> {
  const out = new Map<string, string>();
  if (typeof value === "string") out.set(path, value);
  else if (value && typeof value === "object") {
    for (const [key, child] of Object.entries(value)) for (const [p, v] of leaves(child, path ? `${path}.${key}` : key)) out.set(p, v);
  }
  return out;
}

/** Los {marcadores} y las <etiquetas> de un texto, ordenados. */
const marks = (text: string) => (text.match(/\{\w+\}|<\/?\w+>/g) ?? []).sort();

describe("diccionarios", () => {
  const esLeaves = leaves(es);

  it.each(LOCALES.map((l) => [l]))("%s tiene las mismas claves que el castellano", (locale) => {
    expect([...leaves(getDictionary(locale)).keys()].sort()).toEqual([...esLeaves.keys()].sort());
  });

  it("cada texto en inglés usa los mismos marcadores y etiquetas (salvo las formas en minúscula del castellano)", () => {
    const enLeaves = leaves(en);
    const normalize = (m: string[]) => m.map((x) => x.replace(/Lower\}$/, "}"));
    for (const [path, text] of esLeaves) {
      // {civLower} y {bLower} son una necesidad del castellano ("los britanos"); en inglés van con mayúscula.
      expect(normalize(marks(enLeaves.get(path) ?? "")), path).toEqual(normalize(marks(text)));
    }
  });

  it("los textos sin traducir son solo los que no aplican a un idioma", () => {
    const same = [...esLeaves].filter(([path, text]) => text.length > 24 && leaves(en).get(path) === text).map(([path]) => path);
    expect(same).toEqual(["home.kicker"]);
  });

  it("sin guiones largos", () => {
    for (const locale of LOCALES) for (const [path, text] of leaves(getDictionary(locale))) expect(text.includes("—"), path).toBe(false);
  });

  it("fill reemplaza los marcadores y deja a la vista los que faltan", () => {
    expect(fill("{a} y {b}", { a: 1 })).toBe("1 y {b}");
  });
});

describe("rutas por idioma", () => {
  it("localePath deja el castellano igual y le pone el prefijo a los demás", () => {
    expect(localePath("es", "/biblioteca?civ=x")).toBe("/biblioteca?civ=x");
    expect(localePath("en", "/biblioteca?civ=x")).toBe("/en/biblioteca?civ=x");
    expect(localePath("en", "/")).toBe("/en");
    expect(localePath("en", "/#apoyar")).toBe("/en#apoyar");
  });

  it("stripLocale saca el idioma solo si es uno de los nuestros", () => {
    expect(stripLocale("/en/bo/abc")).toEqual({ locale: "en", path: "/bo/abc", prefixed: true });
    expect(stripLocale("/en")).toEqual({ locale: "en", path: "/", prefixed: true });
    expect(stripLocale("/fr/bo")).toEqual({ locale: "es", path: "/fr/bo", prefixed: false });
    expect(stripLocale("/english")).toEqual({ locale: "es", path: "/english", prefixed: false });
  });

  it("asLocale no confía en lo que llega", () => {
    expect(asLocale("en")).toBe("en");
    expect(asLocale("xx")).toBe("es");
    expect(asLocale(undefined)).toBe("es");
  });

  it("los hreflang dan cada idioma y x-default", () => {
    expect(localeAlternates("en", "/roadmap")).toEqual({
      canonical: "/en/roadmap",
      languages: { es: "/roadmap", en: "/en/roadmap", "x-default": "/roadmap" },
    });
  });
});

describe("el proxy", () => {
  it("las URL de siempre siguen andando en castellano, sin redirigir", () => {
    expect(localeRouting("/")).toEqual({ kind: "rewrite", to: "/es", locale: "es", path: "/" });
    expect(localeRouting("/biblioteca")).toEqual({ kind: "rewrite", to: "/es/biblioteca", locale: "es", path: "/biblioteca" });
    expect(localeRouting("/bo/k7m2qa")).toMatchObject({ kind: "rewrite", to: "/es/bo/k7m2qa" });
    expect(localeRouting("/cruces/britons-vs-franks")).toMatchObject({ kind: "rewrite", to: "/es/cruces/britons-vs-franks" });
  });

  it("/es redirige con 308 a la ruta sin prefijo", () => {
    expect(localeRouting("/es")).toEqual({ kind: "redirect", to: "/", status: 308 });
    expect(localeRouting("/es/biblioteca")).toEqual({ kind: "redirect", to: "/biblioteca", status: 308 });
  });

  it("/en pasa tal cual", () => {
    expect(localeRouting("/en/biblioteca")).toEqual({ kind: "pass", locale: "en", path: "/biblioteca" });
    expect(localeRouting("/en")).toEqual({ kind: "pass", locale: "en", path: "/" });
  });

  it("la cookie lleva de la ruta sin prefijo al idioma elegido, con 307 y nunca al revés", () => {
    expect(localeRouting("/cuenta", "en")).toEqual({ kind: "redirect", to: "/en/cuenta", status: 307 });
    expect(localeRouting("/", "en")).toEqual({ kind: "redirect", to: "/en", status: 307 });
    expect(localeRouting("/en/cuenta", "en")).toMatchObject({ kind: "pass" });
    expect(localeRouting("/en/cuenta", "es")).toMatchObject({ kind: "pass" });
    expect(localeRouting("/cuenta", "es")).toMatchObject({ kind: "rewrite" });
    expect(localeRouting("/cuenta", "xx")).toMatchObject({ kind: "rewrite" });
  });

  it("las imágenes generadas se sirven con el prefijo, también en castellano", () => {
    expect(localeRouting("/es/opengraph-image")).toMatchObject({ kind: "pass", locale: "es" });
    expect(localeRouting("/es/bo/k7m2qa/opengraph-image")).toMatchObject({ kind: "pass", locale: "es" });
    expect(localeRouting("/opengraph-image")).toMatchObject({ kind: "rewrite", to: "/es/opengraph-image" });
    expect(localeRouting("/opengraph-image", "en")).toMatchObject({ kind: "rewrite" });
  });

  it("lo que no pasa por el idioma no se toca", () => {
    for (const path of ["/api/bo", "/auth/callback", "/coach/game_data.json", "/idioma/en", "/_next/static/x.js", "/sitemap.xml", "/robots.txt", "/icons/house.png"]) {
      expect(localeRouting(path, "en"), path).toMatchObject({ kind: "pass" });
    }
  });

  it("lo que no es una página da 404 en el idioma de la ruta", () => {
    expect(localeRouting("/no-existe")).toEqual({ kind: "notFound", to: "/es/404" });
    expect(localeRouting("/en/no-existe")).toEqual({ kind: "notFound", to: "/en/404" });
    expect(localeRouting("/fr/bo")).toEqual({ kind: "notFound", to: "/es/404" });
  });
});
