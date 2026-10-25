import { describe, expect, it } from "vitest";
import { filterValue, libraryHref, MAX_PAGE, pageNumber } from "./filters";

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
