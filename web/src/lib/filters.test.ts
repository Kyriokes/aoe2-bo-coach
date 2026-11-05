import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { filterValue, knownCiv, knownTag, libraryHref, libraryPageFor, MAX_PAGE, pageNumber } from "./filters";

// civ y tag van tal cual al filtro de PostgREST (eq.<civ>, cs.{<tag>}): una llave, una coma o comillas rompen la
// sintaxis y la API respondía 500 (/api/bo?tag=a},b). Se acepta solo lo que puede tener un nombre de civ o una etiqueta.
describe("valores de los filtros civ y tag", () => {
  it("acepta letras con acentos, números, espacios y - _ . +", () => {
    expect(filterValue("feudal")).toBe("feudal");
    expect(filterValue("fast castle")).toBe("fast castle");
    expect(filterValue("22 pop")).toBe("22 pop");
    expect(filterValue("c++")).toBe("c++");
    expect(filterValue("v2.1_beta-x")).toBe("v2.1_beta-x");
    expect(filterValue("Arqueros rápidos")).toBe("Arqueros rápidos");
  });

  it("recorta espacios y descarta lo vacío o demasiado largo", () => {
    expect(filterValue("  arabia ")).toBe("arabia");
    expect(filterValue("   ")).toBeUndefined();
    expect(filterValue(undefined)).toBeUndefined();
    expect(filterValue("a".repeat(41))).toBeUndefined();
    expect(filterValue("a".repeat(40))).toBe("a".repeat(40));
  });

  it("rechaza lo que rompe la sintaxis del filtro", () => {
    for (const bad of ["a},b", "a,b", '"feudal"', "x\u005Cy", "(a)", "a|b", "a;b", "{feudal}"]) {
      expect(filterValue(bad), bad).toBeUndefined();
    }
  });
});

describe("civs y etiquetas conocidas", () => {
  it("una civ del juego o any, sin importar las mayúsculas", () => {
    expect(knownCiv("franks")).toBe("franks");
    expect(knownCiv(" Franks ")).toBe("franks");
    expect(knownCiv("any")).toBe("any");
  });

  it("lo inventado no es una civ, ni lo que heredan todos los objetos", () => {
    for (const bad of ["casino online gratis", "atlantes", "constructor", "", "a,b", undefined]) {
      expect(knownCiv(bad), String(bad)).toBeUndefined();
    }
  });

  it("las etiquetas de la lista, sin importar las mayúsculas", () => {
    expect(knownTag("Feudal")).toBe("feudal");
    expect(knownTag("fast_castle")).toBe("fast_castle");
    expect(knownTag("casino online gratis")).toBeUndefined();
    expect(knownTag(undefined)).toBeUndefined();
  });

  it("se conocen todas las civs y las etiquetas de las BO del repo", () => {
    const repo = join(process.cwd(), "..");
    const folders = ["build_orders", join("build_orders", "civs")];
    const files = folders.flatMap((dir) => readdirSync(join(repo, dir)).filter((f) => f.endsWith(".json")).map((f) => join(repo, dir, f)));
    expect(files.length).toBeGreaterThan(100);
    for (const file of files) {
      const bo = JSON.parse(readFileSync(file, "utf8")) as { civ?: string; tags?: string[] };
      expect(knownCiv(bo.civ ?? "any"), file).toBeDefined();
      for (const tag of bo.tags ?? []) expect(knownTag(tag), `${file}: ${tag}`).toBeDefined();
    }
  });
});

describe("la biblioteca para los buscadores", () => {
  it("una civ o una etiqueta conocida es una página, con su filtro en minúsculas", () => {
    expect(libraryPageFor({})).toEqual({ noindex: false });
    expect(libraryPageFor({ civ: "Franks" })).toEqual({ civ: "franks", noindex: false });
    expect(libraryPageFor({ civ: "any" })).toEqual({ civ: "any", noindex: false });
    expect(libraryPageFor({ tag: "Feudal" })).toEqual({ tag: "feudal", noindex: false });
  });

  it("lo inventado no se indexa y su texto no va a la página", () => {
    expect(libraryPageFor({ civ: "casino online gratis" })).toEqual({ noindex: true });
    expect(libraryPageFor({ tag: "casino online gratis" })).toEqual({ noindex: true });
    expect(libraryPageFor({ civ: "franks", tag: "casino online gratis" })).toEqual({ civ: "franks", noindex: true });
    expect(libraryPageFor({ civ: "casino online gratis", tag: "feudal" })).toEqual({ tag: "feudal", noindex: true });
  });

  it("las búsquedas y una civ con etiqueta tampoco", () => {
    expect(libraryPageFor({ q: "scouts" })).toEqual({ noindex: true });
    expect(libraryPageFor({ q: "scouts", civ: "franks" })).toEqual({ civ: "franks", noindex: true });
    expect(libraryPageFor({ civ: "franks", tag: "feudal" })).toEqual({ civ: "franks", noindex: true });
  });
});

describe("páginas de la biblioteca", () => {
  it("lee ?pagina y vuelve a la primera con lo que no sirve", () => {
    expect(pageNumber("2")).toBe(2);
    expect(pageNumber(" 3 ")).toBe(3);
    expect(pageNumber(String(MAX_PAGE))).toBe(MAX_PAGE);
    for (const bad of [undefined, "", "0", "-1", "1.5", "dos", String(MAX_PAGE + 1), "Infinity"]) {
      expect(pageNumber(bad), String(bad)).toBe(1);
    }
  });

  it("arma la dirección con los filtros y sin lo que es de fábrica", () => {
    expect(libraryHref({})).toBe("/biblioteca");
    expect(libraryHref({ sort: "votos" }, 1)).toBe("/biblioteca");
    expect(libraryHref({}, 2)).toBe("/biblioteca?pagina=2");
    expect(libraryHref({ q: "fast castle", civ: "aztecs", tag: "feudal", sort: "recientes" }, 3)).toBe(
      "/biblioteca?q=fast+castle&civ=aztecs&tag=feudal&orden=recientes&pagina=3",
    );
  });
});
